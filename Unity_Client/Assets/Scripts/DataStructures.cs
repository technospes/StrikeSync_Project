using System.Collections.Generic;

// ============================================================
//  DataStructures.cs  —  v3.0
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
    public float move_x;
    public float move_z;
    public bool jumped;
    public bool punch_left;
    public bool punch_right;
    public float lw_vel;
    public float rw_vel;

    // v14 — calibration fields from Python
    public float calib_score;   // 0..1 fraction of critical joints visible
    public bool calib_ready;   // true when fully calibrated for CALIB_HOLD_FRAMES
}

[System.Serializable]
public class PoseDataPacket
{
    public List<PlayerData> players;
}