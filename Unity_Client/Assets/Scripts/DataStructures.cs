using System.Collections.Generic;

// ============================================================
//  DataStructures.cs  —  v2.0
//  JSON contract between Python pose_server and Unity.
//  Variable names MUST match Python dict keys exactly.
// ============================================================

[System.Serializable]
public class LandmarkData
{
    public float x;
    public float y;
    public float z;
    public float v;   // visibility / confidence
}

[System.Serializable]
public class PlayerData
{
    public int id;
    public List<LandmarkData> landmarks;

    // ── Optional gesture values from the Python VAAS smoother ──────────────
    // Unity's JsonUtility sets these to their default (0 / false) if absent,
    // so always check has_move_x before using move_x.
    public float move_x;     // smoothed walk axis  [-1, +1]
    public float lw_vel;     // left-wrist  velocity (m/s normalised)
    public float rw_vel;     // right-wrist velocity
    public bool jumped;     // true on the frame the jump was detected

    /// <summary>True when the server included a move_x value in this packet.</summary>
    [System.NonSerialized]
    public bool has_move_x;   // set by PoseManager after deserialization

    // JsonUtility doesn't support "field present" detection, so we use a
    // sentinel: if move_x == 0 AND lw_vel == 0 AND rw_vel == 0 it MIGHT
    // mean the fields are absent — but we treat any packet from v7 server
    // as having the field.  PoseManager sets has_move_x = true after parse.
}

[System.Serializable]
public class PoseDataPacket
{
    public List<PlayerData> players;
}