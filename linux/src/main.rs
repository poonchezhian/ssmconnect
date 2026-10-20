fn main() -> std::process::ExitCode { ssmconnect::cli::main(std::env::args().skip(1).collect()) }
