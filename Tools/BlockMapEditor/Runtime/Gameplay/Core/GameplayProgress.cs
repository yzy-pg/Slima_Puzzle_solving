using System;
using UnityEngine;

namespace BlockMap.Gameplay
{
    [Serializable]
    public sealed class GameplayProgress
    {
        public string worldJson;
        public SlimeProgress[] actors;
        public int activeActor;
        public MechanismProgress[] mechanisms;
        public bool won;
    }

    [Serializable]
    public sealed class SlimeProgress
    {
        public Vector3Int cell;
        public int facing;
        public SlimeState state;
    }

    [Serializable]
    public sealed class MechanismProgress
    {
        public string blockId;
        public int receiverFaces;
        public Vector3Int cell;
        public bool toggled;
        public bool latched;
        public float buttonRemaining;
    }
}
