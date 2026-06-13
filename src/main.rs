mod agent;
mod config;
mod error;

use crate::config::Config;

#[tokio::main]
async fn main() -> error::Result<()> {
    Config::init()?;

    let agent = agent::Agent::new(&Config::get().agent);
    let session = agent.start_session().await?;
    println!("session working dir: {}", session.workdir().display());

    let output = session.wait().await?;
    println!("{}", String::from_utf8_lossy(&output.stdout));

    Ok(())
}
