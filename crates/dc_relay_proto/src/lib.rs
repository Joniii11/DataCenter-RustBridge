//! Shared relay protocol for Data Center Multiplayer.

use bincode::{Decode, Encode};

/// Packets exchanged between relay clients and the relay server.
#[derive(Encode, Decode, Debug, Clone)]
pub enum RelayPacket {
    CreateRoom {
        steam_id: u64,
    },
    JoinRoom {
        room_code: String,
        steam_id: u64,
    },
    LeaveRoom,
    GameData {
        payload: Vec<u8>,
    },
    // TODO
    GameDataCheck {
        checksum: u32,
    },
    Heartbeat,

    RoomCreated {
        room_code: String,
    },
    JoinOk {
        host_steam_id: u64,
    },
    RoomNotFound,
    RoomFull,
    PeerJoined {
        steam_id: u64,
    },
    PeerLeft {
        steam_id: u64,
    },
    PeerData {
        sender_steam_id: u64,
        payload: Vec<u8>,
    },
    ServerError {
        message: String,
    },
}

pub const MAX_PACKET_SIZE: usize = 8192;
pub const DEFAULT_PORT: u16 = 9943;
pub const ROOM_CODE_LEN: usize = 6;
pub const MAX_PLAYERS_PER_ROOM: usize = 8;

const BINCODE_CONFIG: bincode::config::Configuration = bincode::config::standard();

/// Encode a packet to wire format: [u32 LE length][bincode data]
pub fn encode_packet(packet: &RelayPacket) -> Option<Vec<u8>> {
    let data = bincode::encode_to_vec(packet, BINCODE_CONFIG).ok()?;
    let len = data.len() as u32;
    let mut buf = Vec::with_capacity(4 + data.len());
    buf.extend_from_slice(&len.to_le_bytes());
    buf.extend_from_slice(&data);
    Some(buf)
}

pub fn encode_ws(packet: &RelayPacket) -> Option<Vec<u8>> {
    bincode::encode_to_vec(packet, BINCODE_CONFIG).ok()
}

pub fn decode_packet(data: &[u8]) -> Option<RelayPacket> {
    bincode::decode_from_slice(data, BINCODE_CONFIG)
        .ok()
        .map(|(pkt, _)| pkt)
}

pub fn read_packet(reader: &mut impl std::io::Read) -> std::io::Result<RelayPacket> {
    let mut len_buf = [0u8; 4];
    reader.read_exact(&mut len_buf)?;
    let len = u32::from_le_bytes(len_buf) as usize;

    if len == 0 || len > MAX_PACKET_SIZE {
        return Err(std::io::Error::new(
            std::io::ErrorKind::InvalidData,
            format!("invalid packet length: {len}"),
        ));
    }

    let mut data = vec![0u8; len];
    reader.read_exact(&mut data)?;

    decode_packet(&data).ok_or_else(|| {
        std::io::Error::new(std::io::ErrorKind::InvalidData, "failed to decode packet")
    })
}

pub fn write_packet(writer: &mut impl std::io::Write, packet: &RelayPacket) -> std::io::Result<()> {
    let buf = encode_packet(packet).ok_or_else(|| {
        std::io::Error::new(std::io::ErrorKind::InvalidData, "failed to encode packet")
    })?;
    writer.write_all(&buf)?;
    writer.flush()
}

pub fn generate_room_code() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let seed = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u64;

    let thread_id = std::thread::current().id();
    let extra = format!("{:?}", thread_id);
    let extra_hash: u64 = extra
        .bytes()
        .fold(0u64, |acc, b| acc.wrapping_mul(31).wrapping_add(b as u64));

    let chars: &[u8] = b"ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    let mut code = String::with_capacity(ROOM_CODE_LEN);
    let mut s = seed.wrapping_add(extra_hash);
    for _ in 0..ROOM_CODE_LEN {
        s = s
            .wrapping_mul(6364136223846793005)
            .wrapping_add(1442695040888963407);
        let idx = (s >> 33) as usize % chars.len();
        code.push(chars[idx] as char);
    }
    code
}
