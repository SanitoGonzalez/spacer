use std::process::Stdio;

use tokio::process::Command;

use crate::agent::{Config, Session};
use crate::error::Result;

pub struct Codex {
    config: &'static Config,
}

impl Codex {
    pub fn new(config: &'static Config) -> Self {
        Self { config }
    }

    pub async fn start_session(&self) -> Result<Session> {
        let workdir = tempfile::tempdir()?;

        let child = Command::new("codex")
            .arg("exec")
            .arg("--model")
            .arg(&self.config.model)
            .current_dir(workdir.path())
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true)
            .spawn()?;

        Ok(Session::new(workdir, child))
    }
}
