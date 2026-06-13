use std::path::Path;
use std::process::Output;

use serde::Deserialize;
use tempfile::TempDir;
use tokio::process::Child;

use crate::error::Result;

mod codex;

#[derive(Debug, Clone, Copy, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Kind {
    Codex,
}

#[derive(Debug, Deserialize)]
pub struct Config {
    pub kind: Kind,
    pub model: String,
}

pub enum Agent {
    Codex(codex::Codex),
}

impl Agent {
    pub fn new(config: &'static Config) -> Self {
        match config.kind {
            Kind::Codex => Agent::Codex(codex::Codex::new(config)),
        }
    }

    pub async fn start_session(&self) -> Result<Session> {
        match self {
            Agent::Codex(backend) => backend.start_session().await,
        }
    }
}

pub struct Session {
    workdir: TempDir,
    child: Child,
}

impl Session {
    pub(crate) fn new(workdir: TempDir, child: Child) -> Self {
        Self { workdir, child }
    }

    pub fn workdir(&self) -> &Path {
        self.workdir.path()
    }

    pub async fn wait(self) -> Result<Output> {
        Ok(self.child.wait_with_output().await?)
    }
}
