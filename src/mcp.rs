use std::net::SocketAddr;
use std::sync::Arc;

use rmcp::handler::server::wrapper::Parameters;
use rmcp::transport::streamable_http_server::session::local::LocalSessionManager;
use rmcp::transport::streamable_http_server::{StreamableHttpServerConfig, StreamableHttpService};
use rmcp::{ErrorData as McpError, ServerHandler, schemars, tool, tool_handler, tool_router};
use serde::Deserialize;
use tokio::sync::Mutex;
use tokio::task::JoinHandle;
use tokio_util::sync::CancellationToken;

/// Path the Streamable HTTP MCP endpoint is mounted at.
const MCP_PATH: &str = "/mcp";

#[derive(Debug, Deserialize)]
pub struct Config {
    pub port: u16,
}

/// Bind the listener and start serving the MCP endpoint in a background task.
///
/// The socket is bound before this returns, so the caller knows the server is
/// reachable (and on which address) the moment it gets the handle. The accept
/// loop then runs on a spawned task until the returned [`ServerHandle`] is
/// asked to [`shutdown`](ServerHandle::shutdown).
pub async fn serve() -> crate::error::Result<ServerHandle> {
    let port = crate::config::Config::get().mcp.port;
    let cancel = CancellationToken::new();

    // Shared across every client session: the factory below runs once per
    // session, and each clone of the `Arc` points at this same counter.
    let counter = Arc::new(Mutex::new(0));

    let service = StreamableHttpService::new(
        move || {
            Ok(Handler {
                counter: counter.clone(),
            })
        },
        LocalSessionManager::default().into(),
        StreamableHttpServerConfig::default().with_cancellation_token(cancel.child_token()),
    );

    let router = axum::Router::new().nest_service(MCP_PATH, service);

    let addr = SocketAddr::from(([127, 0, 0, 1], port));
    let listener = tokio::net::TcpListener::bind(addr).await?;
    let addr = listener.local_addr()?;

    let shutdown = cancel.clone();
    let task = tokio::spawn(async move {
        let _ = axum::serve(listener, router)
            .with_graceful_shutdown(async move { shutdown.cancelled().await })
            .await;
    });

    Ok(ServerHandle { addr, cancel, task })
}

/// Handle to a running [`Server`]. Dropping it detaches the server (it keeps
/// running); call [`shutdown`](Self::shutdown) to stop it gracefully.
pub struct ServerHandle {
    addr: SocketAddr,
    // Held so the server can be stopped via `shutdown`; unused until a caller
    // wires up graceful shutdown.
    #[allow(dead_code)]
    cancel: CancellationToken,
    #[allow(dead_code)]
    task: JoinHandle<()>,
}

impl ServerHandle {
    /// The address the server is listening on, including the resolved port.
    pub fn addr(&self) -> SocketAddr {
        self.addr
    }

    /// Signal the server to stop and wait for the background task to finish.
    #[allow(dead_code)]
    pub async fn shutdown(self) {
        self.cancel.cancel();
        let _ = self.task.await;
    }
}

/// Arguments for the `echo` tool. Field doc comments become the JSON-schema
/// property descriptions clients see.
#[derive(Debug, Deserialize, schemars::JsonSchema)]
struct EchoArgs {
    /// Text to echo back.
    message: String,
    /// How many times to repeat the message.
    #[serde(default = "default_count")]
    count: u32,
}

fn default_count() -> u32 {
    1
}

/// Arguments for the `divide` tool.
#[derive(Debug, Deserialize, schemars::JsonSchema)]
struct DivideArgs {
    /// Dividend.
    a: i64,
    /// Divisor; must be non-zero.
    b: i64,
}

/// Per-session MCP request handler. A fresh instance is created for each client
/// session by the service factory in [`Server::serve`].
#[derive(Clone)]
struct Handler {
    /// Counter shared across every session (see [`Server::serve`]).
    counter: Arc<Mutex<i64>>,
}

#[tool_router]
impl Handler {
    #[tool(description = "Health check; returns \"ok\" if the server is alive.")]
    async fn ping(&self) -> String {
        "ok".to_string()
    }

    /// A tool that takes arguments: `Parameters<T>` deserializes the request's
    /// arguments into `T`, whose schema is published to clients automatically.
    #[tool(description = "Echo a message back, optionally repeated.")]
    async fn echo(&self, Parameters(EchoArgs { message, count }): Parameters<EchoArgs>) -> String {
        message.repeat(count as usize)
    }

    /// A tool that can fail: returning `Err(McpError)` surfaces a proper tool
    /// error to the client rather than a transport-level failure.
    #[tool(description = "Divide a by b; errors when b is zero.")]
    async fn divide(
        &self,
        Parameters(DivideArgs { a, b }): Parameters<DivideArgs>,
    ) -> Result<String, McpError> {
        if b == 0 {
            return Err(McpError::invalid_params("division by zero", None));
        }
        Ok((a / b).to_string())
    }

    /// A stateful tool: the shared counter persists across calls and across
    /// sessions for as long as the server runs.
    #[tool(description = "Increment a shared counter and return its new value.")]
    async fn bump(&self) -> String {
        let mut n = self.counter.lock().await;
        *n += 1;
        n.to_string()
    }
}

#[tool_handler(name = "worlder", version = "0.1.0")]
impl ServerHandler for Handler {}
