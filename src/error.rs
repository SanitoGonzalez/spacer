#[derive(Debug, thiserror::Error)]
pub enum Error {
    #[error(transparent)]
    Io(#[from] std::io::Error),

    #[error("failed to parse config: {0}")]
    Config(#[from] toml::de::Error),
}

pub type Result<T> = std::result::Result<T, Error>;
