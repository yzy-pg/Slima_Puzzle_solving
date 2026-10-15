using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Gameplay.Tests
{
    public sealed class SynchronousFusionTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root;
        BlockWorld _world;
        SlimePartyController _party;
        SlimeActor _blue;
        SlimeActor _partner;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SynchronousFusionTests");
            _world = _root.AddComponent<BlockWorld>();
            for (int x = 0; x < 8; x++)
                for (int z = 0; z < 4; z++)
                    _world.Set(new Vector3Int(x, 0, z), BlockLibrary.IndexOf("Lab_WallBrick1"));
            var gameplay = _root.AddComponent<BlockGameplayController>();
            gameplay.Initialize(_world);
            _party = _root.AddComponent<SlimePartyController>();
            _party.Initialize(gameplay, null, new Vector3Int(1, 1, 1), 1, 1f, 0f, true);
            gameplay.BindParty(_party);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        void ArrangePair(bool controlBlue, SlimeElement partnerElement = SlimeElement.Red, int partnerZ = 1)
        {
            _party.RestoreProgress(new[]
            {
                new SlimeProgress { cell = new Vector3Int(1, 1, 1), facing = 1,
                    state = new SlimeState { size = SlimeSize.Small, element = SlimeElement.Blue } },
                new SlimeProgress { cell = new Vector3Int(2, 1, partnerZ), facing = 1,
                    state = new SlimeState { size = SlimeSize.Small, element = partnerElement } }
            }, controlBlue ? 0 : 1);
            _blue = _party.Actors[0];
            _partner = _party.Actors[1];
        }

        void StartSynchronousMove()
        {
            var route = new List<Vector3Int>();
            SlimeGridMover controlled = _party.ActiveActor.Mover;
            if (_party.TryBuildRoute(controlled, 1, route)) controlled.QueueRoute(route);
            Assert.That(_party.IsBusy, Is.True);
        }

        static void FinishStep(SlimeGridMover mover)
        {
            Assert.That(mover.IsMoving, Is.True);
            typeof(SlimeGridMover).GetField("_progress", PrivateInstance).SetValue(mover, 1f);
            typeof(SlimeGridMover).GetMethod("AdvanceStep", PrivateInstance).Invoke(mover, null);
        }

        static void BeginStep(SlimeGridMover mover) => typeof(SlimeGridMover)
            .GetMethod("BeginNextStep", PrivateInstance).Invoke(mover, null);

        void FinishMovement(bool reverseOrder)
        {
            for (int tick = 0; tick < 40 && _party.IsBusy; tick++)
            {
                var actors = new List<SlimeActor>(_party.Actors);
                if (reverseOrder) actors.Reverse();
                foreach (SlimeActor actor in actors)
                {
                    if (actor == null || !actor.gameObject.activeInHierarchy) continue;
                    if (actor.Mover.IsMoving) FinishStep(actor.Mover);
                    else if (actor.Mover.HasQueuedRoute) BeginStep(actor.Mover);
                }
            }
            Assert.That(_party.IsBusy, Is.False, "融合后不得留下待执行路线或移动锁。");
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void SlidingBlueCatchesPartnerAfterItsSynchronousStep(bool controlBlue, bool reverseOrder)
        {
            ArrangePair(controlBlue);
            SlimeActor controlled = _party.ActiveActor;
            StartSynchronousMove();
            FinishMovement(reverseOrder);

            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(_party.ActiveActor, Is.SameAs(controlled));
            Assert.That(controlled.State.size, Is.EqualTo(SlimeSize.Large));
            Assert.That(controlled.State.element, Is.EqualTo(SlimeElement.Neutral));
            Assert.That(controlled.Mover.Cell, Is.EqualTo(new Vector3Int(3, 1, 1)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BlockedPartnerFusesWhenBlueSlidesIntoItsCell(bool controlBlue)
        {
            ArrangePair(controlBlue);
            _world.Set(new Vector3Int(3, 1, 1), BlockLibrary.IndexOf("Lab_WallBrick1"));
            SlimeActor controlled = _party.ActiveActor;
            StartSynchronousMove();
            FinishMovement(false);

            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(_party.ActiveActor, Is.SameAs(controlled));
            Assert.That(controlled.Mover.Cell, Is.EqualTo(new Vector3Int(2, 1, 1)));
        }

        [Test]
        public void FasterBlueWaitsForPartnersActualArrivalBeforeFusion()
        {
            ArrangePair(true);
            StartSynchronousMove();
            FinishStep(_blue.Mover);
            Assert.That(_party.Actors.Count, Is.EqualTo(2), "同伴正在离开旧格，不能在其身后误融合。");
            Assert.That(_blue.Mover.HasQueuedRoute, Is.True);

            BeginStep(_blue.Mover);
            FinishStep(_blue.Mover);
            Assert.That(_blue.Mover.Cell, Is.EqualTo(_partner.Mover.DestinationCell));
            Assert.That(_party.Actors.Count, Is.EqualTo(2));
            Assert.That(_blue.Mover.HasPendingMovement, Is.False);
            FinishStep(_partner.Mover);

            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(_party.ActiveActor, Is.SameAs(_blue));
            Assert.That(_blue.Mover.HasPendingMovement, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TwoBlueSlimesFuseOnlyWhenTheyReachTheSameEndCell(bool reverseOrder)
        {
            ArrangePair(true, SlimeElement.Blue);
            StartSynchronousMove();
            FinishMovement(reverseOrder);

            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(_party.ActiveActor.Mover.Cell, Is.EqualTo(new Vector3Int(7, 1, 1)));
            Assert.That(_party.ActiveActor.State.element, Is.EqualTo(SlimeElement.Blue));
        }

        [Test]
        public void ParallelBlueSlidesDoNotFuse()
        {
            ArrangePair(true, SlimeElement.Blue, partnerZ: 2);
            StartSynchronousMove();
            FinishMovement(false);

            Assert.That(_party.Actors.Count, Is.EqualTo(2));
            Assert.That(_blue.Mover.Cell, Is.EqualTo(new Vector3Int(7, 1, 1)));
            Assert.That(_partner.Mover.Cell, Is.EqualTo(new Vector3Int(7, 1, 2)));
        }

        [Test]
        public void RestoringPreCollisionProgressRecoversBothSmallSlimes()
        {
            ArrangePair(true);
            SlimeProgress[] saved = _party.CaptureProgress(out int active);
            StartSynchronousMove();
            FinishMovement(false);
            Assert.That(_party.Actors.Count, Is.EqualTo(1));

            _party.RestoreProgress(saved, active);

            Assert.That(_party.Actors.Count, Is.EqualTo(2));
            Assert.That(_party.Actors[0].State.size, Is.EqualTo(SlimeSize.Small));
            Assert.That(_party.Actors[1].State.size, Is.EqualTo(SlimeSize.Small));
            Assert.That(_party.Actors[0].Mover.Cell, Is.EqualTo(new Vector3Int(1, 1, 1)));
            Assert.That(_party.Actors[1].Mover.Cell, Is.EqualTo(new Vector3Int(2, 1, 1)));
            Assert.That(_party.IsBusy, Is.False);
        }
    }
}
