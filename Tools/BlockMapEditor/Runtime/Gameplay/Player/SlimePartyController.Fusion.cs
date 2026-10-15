using UnityEngine;

namespace BlockMap.Gameplay
{
    public sealed partial class SlimePartyController
    {
        static bool CanFusePair(SlimeActor first, SlimeActor second) =>
            first != null && second != null && first != second &&
            first.State.form == SlimeForm.Solid && second.State.form == SlimeForm.Solid &&
            first.State.size == SlimeSize.Small && second.State.size == SlimeSize.Small;

        SlimeActor FindFusionPartnerAt(SlimeActor source, Vector3Int cell)
        {
            foreach (SlimeActor other in _actors)
                if (CanFusePair(source, other) && other.Mover.DestinationCell == cell) return other;
            return null;
        }

        /// <summary>兼容已有融合交互；普通移动碰撞同样可以融合。</summary>
        public bool CanFuseFacing(SlimeActor source)
        {
            if (source == null || IsBusy) return false;
            Vector3Int target = source.Mover.Cell + GridDirection.ToVector(source.Mover.Facing);
            return CanFusePair(source, FindAt(target)) &&
                   _gameplay.CanEnter(source, target, source.Mover.Facing, false);
        }

        public bool TryFuseFacing(SlimeActor source)
        {
            if (!CanFuseFacing(source)) return false;
            Vector3Int target = source.Mover.Cell + GridDirection.ToVector(source.Mover.Facing);
            if (!source.Mover.TryMoveTo(target)) return false;
            Changed?.Invoke();
            return true;
        }

        void FuseAt(Vector3Int cell)
        {
            SlimeActor first = null;
            SlimeActor second = null;
            foreach (SlimeActor actor in _actors)
            {
                // 移动中的 Cell 仍是出发格，必须等真正落在同一格，避免在同伴身后误融合。
                if (actor == null || actor.Mover.IsMoving || actor.Mover.Cell != cell) continue;
                if (first == null) first = actor;
                else { second = actor; break; }
            }
            if (!CanFusePair(first, second)) return;

            SlimeActor survivor = first.IsControlled ? first : second.IsControlled ? second : first;
            SlimeActor removed = survivor == first ? second : first;
            SlimeState fused = SlimeRules.Fuse(first.State, second.State);
            survivor.Mover.Teleport(cell, survivor.Mover.Facing);
            survivor.SetState(fused);
            removed.Mover.StopAfterCurrentStep();
            removed.gameObject.SetActive(false);
            _actors.Remove(removed);
            if (Application.isPlaying) Destroy(removed.gameObject);
            else DestroyImmediate(removed.gameObject);
            _activeIndex = Mathf.Max(0, _actors.IndexOf(survivor));
            RefreshControl();
        }
    }
}
