use std::sync::OnceLock;

use serde::Deserialize;

use crate::agent;
use crate::error::Result;
use crate::mcp;

static CONFIG: OnceLock<Config> = OnceLock::new();

#[derive(Debug, Deserialize)]
pub struct Config {
    pub agent: agent::Config,
    pub mcp: mcp::Config,
}

impl Config {
    pub fn init() -> Result<()> {
        let config: Config = toml::from_str(&std::fs::read_to_string("config.toml")?)?;

        Ok(CONFIG.set(config).expect("config already initialized"))
    }

    pub fn get() -> &'static Self {
        CONFIG.get().expect("config not initialized")
    }
}
