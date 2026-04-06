/// Quaternion rotation
#[derive(Debug, Clone, Copy)]
#[repr(C)]
pub struct Quat {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub w: f32,
}

impl Quat {
    pub fn new(x: f32, y: f32, z: f32, w: f32) -> Self {
        Self { x, y, z, w }
    }

    pub const fn identity() -> Self {
        Self {
            x: 0.0,
            y: 0.0,
            z: 0.0,
            w: 1.0,
        }
    }
}

impl From<(f32, f32, f32, f32)> for Quat {
    fn from((x, y, z, w): (f32, f32, f32, f32)) -> Self {
        Self { x, y, z, w }
    }
}

impl From<Quat> for (f32, f32, f32, f32) {
    fn from(q: Quat) -> Self {
        (q.x, q.y, q.z, q.w)
    }
}

impl From<&(f32, f32, f32, f32)> for Quat {
    fn from((x, y, z, w): &(f32, f32, f32, f32)) -> Self {
        Self {
            x: *x,
            y: *y,
            z: *z,
            w: *w,
        }
    }
}
