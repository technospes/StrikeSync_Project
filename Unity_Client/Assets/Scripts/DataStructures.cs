using System.Collections.Generic;

// This file defines the "contract" between Python and C#.
// The variable names here MUST exactly match the JSON keys from Python.
// We must use [System.Serializable] for JsonUtility to see these classes.

[System.Serializable]
public class LandmarkData
{
    public float x;
    public float y;
    public float z;
    public float v; // visibility
}

[System.Serializable]
public class PlayerData
{
    public int id;
    public List<LandmarkData> landmarks;
}

[System.Serializable]
public class PoseDataPacket
{
    public List<PlayerData> players;
}