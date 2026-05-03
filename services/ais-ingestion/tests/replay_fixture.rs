//! Integration test: replay the bundled NMEA fixture through the parser
//! end-to-end without any external services.

use std::path::PathBuf;

use ais::{AisFragments, AisParser};
use tokio::fs::File;
use tokio::io::{AsyncBufReadExt, BufReader};

#[tokio::test]
async fn fixture_lines_parse_or_are_classified() {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("tests")
        .join("fixtures")
        .join("sample.nmea");
    let file = File::open(&path)
        .await
        .unwrap_or_else(|err| panic!("open {}: {err}", path.display()));
    let reader = BufReader::new(file);
    let mut lines = reader.lines();

    let mut parser = AisParser::new();
    let mut total = 0_usize;
    let mut complete = 0_usize;
    let mut incomplete = 0_usize;

    while let Some(line) = lines.next_line().await.expect("read line") {
        if line.trim().is_empty() {
            continue;
        }
        total += 1;
        match parser.parse(line.as_bytes(), true) {
            Ok(AisFragments::Complete(_)) => complete += 1,
            Ok(AisFragments::Incomplete(_)) => incomplete += 1,
            Err(err) => panic!("decode failed on line `{line}`: {err}"),
        }
    }

    assert!(total > 0, "fixture must contain at least one sentence");
    assert!(
        complete > 0,
        "fixture must contain at least one complete sentence (got {complete} complete, {incomplete} fragments)"
    );
}
