//! NMEA AIVDM decoding pipeline using the [`ais`] crate.
//!
//! Reads raw NMEA lines, hands them to [`ais::AisParser`], and emits
//! normalized [`DecodedMessage`]s for both the Postgres writer and the
//! Redis Streams publisher.

use std::sync::Arc;

use ais::messages::AisMessage;
use ais::{AisFragments, AisParser};
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use tokio::sync::mpsc;
use tracing::{debug, trace, warn};

use crate::stream::RedisStreamPublisher;
use crate::telemetry::Metrics;

/// A decoded AIS message normalized into `FjordWatch`'s wire format.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct DecodedMessage {
    pub mmsi: i64,
    pub ts: DateTime<Utc>,
    pub message_type: u8,
    pub channel: Option<char>,
    /// When set, payload contains a position fix.
    pub position: Option<Position>,
    /// When set, payload contains static / voyage data.
    pub static_data: Option<StaticData>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Position {
    pub latitude: f64,
    pub longitude: f64,
    pub speed_over_ground: Option<f32>,
    pub course_over_ground: Option<f32>,
    pub true_heading: Option<u16>,
    pub rate_of_turn_deg_per_min: Option<f32>,
    pub navigation_status: Option<u8>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Default)]
pub struct StaticData {
    pub name: Option<String>,
    pub call_sign: Option<String>,
    pub imo: Option<u32>,
    pub ship_type: Option<u8>,
    pub dim_to_bow: Option<u16>,
    pub dim_to_stern: Option<u16>,
    pub dim_to_port: Option<u16>,
    pub dim_to_starboard: Option<u16>,
    pub destination: Option<String>,
    pub draught_m: Option<f32>,
    pub eta_month: Option<u8>,
    pub eta_day: Option<u8>,
    pub eta_hour: Option<u8>,
    pub eta_minute: Option<u8>,
}

/// Run the decoder loop until `raw_rx` is closed.
///
/// Each decoded message is published to Redis (best-effort) and forwarded to
/// the Postgres writer. Decode failures are counted in `Metrics::decode_errors`
/// and logged at TRACE; partial fragments are logged at DEBUG.
pub async fn run(
    mut raw_rx: mpsc::Receiver<String>,
    decoded_tx: mpsc::Sender<DecodedMessage>,
    mut publisher: RedisStreamPublisher,
    metrics: Arc<Metrics>,
) -> anyhow::Result<()> {
    let mut parser = AisParser::new();

    while let Some(line) = raw_rx.recv().await {
        let line_trim = line.trim();
        if line_trim.is_empty() {
            continue;
        }
        let parsed = parser.parse(line_trim.as_bytes(), true);
        match parsed {
            Ok(AisFragments::Complete(sentence)) => {
                let Some(message) = sentence.message else {
                    continue;
                };
                let Some(msg) = normalize(&message, sentence.channel) else {
                    metrics.unsupported.increment(1);
                    continue;
                };
                metrics.decoded.increment(1);
                if let Err(err) = publisher.publish(&msg).await {
                    warn!(error = %err, "redis publish failed; dropping message");
                    metrics.publish_errors.increment(1);
                }
                if decoded_tx.send(msg).await.is_err() {
                    debug!("store sink closed; exiting decoder");
                    break;
                }
            }
            Ok(AisFragments::Incomplete(_)) => {
                metrics.partial.increment(1);
            }
            Err(err) => {
                metrics.decode_errors.increment(1);
                trace!(error = %err, line = %line_trim, "decode error");
            }
        }
    }

    Ok(())
}

/// Normalize an [`AisMessage`] into the `FjordWatch` [`DecodedMessage`] shape.
/// Returns `None` for AIS message types we do not care about.
#[allow(clippy::cast_possible_truncation, clippy::cast_lossless)]
fn normalize(message: &AisMessage, channel: Option<char>) -> Option<DecodedMessage> {
    let now = Utc::now();
    match message {
        AisMessage::PositionReport(p) => Some(DecodedMessage {
            mmsi: i64::from(p.mmsi),
            ts: now,
            message_type: p.message_type,
            channel,
            position: position_from_class_a(p),
            static_data: None,
        }),
        AisMessage::StandardClassBPositionReport(p) => Some(DecodedMessage {
            mmsi: i64::from(p.mmsi),
            ts: now,
            message_type: p.message_type,
            channel,
            position: position_from_class_b(p),
            static_data: None,
        }),
        AisMessage::ExtendedClassBPositionReport(p) => Some(DecodedMessage {
            mmsi: i64::from(p.mmsi),
            ts: now,
            message_type: p.message_type,
            channel,
            position: position_from_extended_class_b(p),
            static_data: Some(StaticData {
                name: ascii_to_string(&p.name),
                ship_type: p.type_of_ship_and_cargo.map(ship_type_to_u8),
                dim_to_bow: Some(p.dimension_to_bow),
                dim_to_stern: Some(p.dimension_to_stern),
                dim_to_port: Some(p.dimension_to_port),
                dim_to_starboard: Some(p.dimension_to_starboard),
                ..Default::default()
            }),
        }),
        AisMessage::StaticAndVoyageRelatedData(s) => Some(DecodedMessage {
            mmsi: i64::from(s.mmsi),
            ts: now,
            message_type: s.message_type,
            channel,
            position: None,
            static_data: Some(StaticData {
                name: ascii_to_string(&s.vessel_name),
                call_sign: ascii_to_string(&s.callsign),
                imo: Some(s.imo_number),
                ship_type: s.ship_type.map(ship_type_to_u8),
                dim_to_bow: Some(s.dimension_to_bow),
                dim_to_stern: Some(s.dimension_to_stern),
                dim_to_port: Some(s.dimension_to_port),
                dim_to_starboard: Some(s.dimension_to_starboard),
                destination: ascii_to_string(&s.destination),
                draught_m: Some(s.draught),
                eta_month: s.eta_month_utc,
                eta_day: s.eta_day_utc,
                eta_hour: Some(s.eta_hour_utc),
                eta_minute: s.eta_minute_utc,
            }),
        }),
        AisMessage::StaticDataReport(s) => {
            let mut sd = StaticData::default();
            match &s.message_part {
                ais::messages::static_data_report::MessagePart::PartA { vessel_name } => {
                    sd.name = ascii_to_string(vessel_name);
                }
                ais::messages::static_data_report::MessagePart::PartB {
                    ship_type,
                    callsign,
                    dimension_to_bow,
                    dimension_to_stern,
                    dimension_to_port,
                    dimension_to_starboard,
                    ..
                } => {
                    sd.ship_type = (*ship_type).map(ship_type_to_u8);
                    sd.call_sign = ascii_to_string(callsign);
                    sd.dim_to_bow = Some(*dimension_to_bow);
                    sd.dim_to_stern = Some(*dimension_to_stern);
                    sd.dim_to_port = Some(*dimension_to_port);
                    sd.dim_to_starboard = Some(*dimension_to_starboard);
                }
                ais::messages::static_data_report::MessagePart::Unknown(_) => {}
            }
            Some(DecodedMessage {
                mmsi: i64::from(s.mmsi),
                ts: now,
                message_type: s.message_type,
                channel,
                position: None,
                static_data: Some(sd),
            })
        }
        // Other types (4 base station, 8/15/17/20/21 etc.) are valid AIS but
        // outside FjordWatch's scope for now.
        _ => None,
    }
}

fn position_from_class_a(p: &ais::messages::position_report::PositionReport) -> Option<Position> {
    let lat = p.latitude?;
    let lon = p.longitude?;
    Some(Position {
        latitude: f64::from(lat),
        longitude: f64::from(lon),
        speed_over_ground: p.speed_over_ground,
        course_over_ground: p.course_over_ground,
        true_heading: p.true_heading,
        // The ais crate keeps RateOfTurn in a private module; we surface
        // it once that type is publicly reachable. Phase 3 anomaly
        // detection will compute its own rate-of-turn from successive
        // headings, so we lose nothing by deferring this field.
        rate_of_turn_deg_per_min: None,
        navigation_status: p.navigation_status.map(navigation_status_to_u8),
    })
}

fn position_from_class_b(
    p: &ais::messages::standard_class_b_position_report::StandardClassBPositionReport,
) -> Option<Position> {
    let lat = p.latitude?;
    let lon = p.longitude?;
    Some(Position {
        latitude: f64::from(lat),
        longitude: f64::from(lon),
        speed_over_ground: p.speed_over_ground,
        course_over_ground: p.course_over_ground,
        true_heading: p.true_heading,
        rate_of_turn_deg_per_min: None,
        navigation_status: None,
    })
}

fn position_from_extended_class_b(
    p: &ais::messages::extended_class_b_position_report::ExtendedClassBPositionReport,
) -> Option<Position> {
    let lat = p.latitude?;
    let lon = p.longitude?;
    Some(Position {
        latitude: f64::from(lat),
        longitude: f64::from(lon),
        speed_over_ground: p.speed_over_ground,
        course_over_ground: p.course_over_ground,
        true_heading: p.true_heading,
        rate_of_turn_deg_per_min: None,
        navigation_status: None,
    })
}

fn ascii_to_string<S: AsRef<[u8]>>(s: &S) -> Option<String> {
    let bytes = s.as_ref();
    let trimmed = std::str::from_utf8(bytes)
        .unwrap_or("")
        .trim_matches(|c: char| c == '@' || c.is_whitespace());
    if trimmed.is_empty() {
        None
    } else {
        Some(trimmed.to_string())
    }
}

const fn navigation_status_to_u8(status: ais::messages::position_report::NavigationStatus) -> u8 {
    use ais::messages::position_report::NavigationStatus as NS;
    match status {
        NS::UnderWayUsingEngine => 0,
        NS::AtAnchor => 1,
        NS::NotUnderCommand => 2,
        NS::RestrictedManouverability => 3,
        NS::ConstrainedByDraught => 4,
        NS::Moored => 5,
        NS::Aground => 6,
        NS::EngagedInFishing => 7,
        NS::UnderWaySailing => 8,
        NS::ReservedForHSC => 9,
        NS::ReservedForWIG => 10,
        NS::Reserved01 => 11,
        NS::Reserved02 => 12,
        NS::Reserved03 => 13,
        NS::AisSartIsActive => 14,
        NS::Unknown(n) => n,
    }
}

fn ship_type_to_u8(ship_type: ais::messages::types::ShipType) -> u8 {
    // The ais crate's ShipType is an enum without a stable numeric reachable
    // through the public API. Round-trip through Debug to extract the raw
    // wire code; this is sufficient for portfolio reporting and avoids a
    // hand-rolled enum mapping that would drift with the upstream crate.
    let dbg = format!("{ship_type:?}");
    dbg.strip_prefix("ShipType(")
        .and_then(|rest| rest.strip_suffix(')'))
        .and_then(|s| s.parse::<u8>().ok())
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Single-fragment Type 3 position report (taken verbatim from the
    /// `ais` crate's `NO_CHANNEL` regression sentence). Type 3 decodes via
    /// the same `PositionReport` struct as types 1 and 2.
    const TYPE3_LINE: &str = "!AIVDM,1,1,,,34RvgN500005tLTMfjiTs3u`0>`<,0*7A";

    #[test]
    fn decode_class_a_position_report_yields_position() {
        let mut parser = AisParser::new();
        let frag = parser.parse(TYPE3_LINE.as_bytes(), true).expect("parse ok");
        let sentence = match frag {
            AisFragments::Complete(s) => s,
            AisFragments::Incomplete(_) => panic!("unexpected incomplete fragment"),
        };
        let message = sentence.message.expect("message decoded");
        let normalized = normalize(&message, sentence.channel).expect("normalized");
        assert_eq!(normalized.message_type, 3);
        assert!(normalized.position.is_some(), "expected position");
        let p = normalized.position.unwrap();
        assert!(p.latitude.is_finite() && p.longitude.is_finite());
        assert!(normalized.mmsi > 0);
    }

    #[test]
    fn decode_aid_to_navigation_returns_none() {
        // Type 21 - aid to navigation. Outside FjordWatch's scope; should return None.
        let line = b"!AIVDM,1,1,,B,E>kb9O9aS@7PUh10dh19@;0Tah2cWrfP:l?M`00003vP100,0*01";
        let mut parser = AisParser::new();
        let frag = parser.parse(line, true).expect("parse ok");
        let sentence = match frag {
            AisFragments::Complete(s) => s,
            AisFragments::Incomplete(_) => panic!("unexpected incomplete fragment"),
        };
        let message = sentence.message.expect("decoded");
        assert!(normalize(&message, sentence.channel).is_none());
    }

    #[test]
    fn ascii_to_string_strips_padding() {
        let raw: Vec<u8> = b"NORWEGIAN STAR@@@@".to_vec();
        let result = ascii_to_string(&raw);
        assert_eq!(result.as_deref(), Some("NORWEGIAN STAR"));
    }

    #[test]
    fn ascii_to_string_empty_returns_none() {
        let raw: Vec<u8> = b"@@@@@@@".to_vec();
        assert!(ascii_to_string(&raw).is_none());
    }
}
