using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Kinex.FX;

namespace Kinex.BattleGame
{
    /// <summary>
    /// Runtime behaviour for one procedural monster body (see MonsterMeshBuilder): a gentle
    /// squash-and-stretch idle bob, a red hit-flash + squash on damage, a brighter pulsing glow
    /// while telegraphing its attack, and a shrink+spin+pop defeat animation with a star burst.
    /// Built fresh per monster by BattleDirector.SpawnMonster — never reused across fights.
    /// </summary>
    public class MonsterBlob : MonoBehaviour
    {
        const float IdleBobAmplitude = 0.05f;
        const float IdleBobSpeed = 1.6f;

        GameObject _body;
        Transform _bodyRoot;
        Vector3 _baseScale;
        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<Color> _baseEmission = new List<Color>();
        Color _tint;
        float _bobPhase;
        Coroutine _flashRoutine;
        Coroutine _telegraphRoutine;
        bool _defeated;

        public static MonsterBlob Spawn(Transform parent, MonsterShape shape, Color tint)
        {
            var go = new GameObject("Monster");
            go.transform.SetParent(parent, false);
            var blob = go.AddComponent<MonsterBlob>();
            blob.Build(shape, tint);
            return blob;
        }

        void Build(MonsterShape shape, Color tint)
        {
            _tint = tint;
            _body = MonsterMeshBuilder.Build(shape, tint);
            _body.transform.SetParent(transform, false);
            _bodyRoot = _body.transform;
            _baseScale = _bodyRoot.localScale;
            _bobPhase = UnityEngine.Random.Range(0f, 6.28f);

            _renderers.Clear();
            _baseEmission.Clear();
            foreach (var r in _body.GetComponentsInChildren<Renderer>())
            {
                _renderers.Add(r);
                var mat = r.material; // instance — safe to mutate per-monster
                _baseEmission.Add(mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black);
            }
        }

        void Update()
        {
            if (_defeated || _bodyRoot == null) return;
            _bobPhase += Time.deltaTime * IdleBobSpeed;
            float bob = Mathf.Sin(_bobPhase) * IdleBobAmplitude;
            transform.localPosition = new Vector3(transform.localPosition.x,
                                                   Mathf.Abs(bob) * 0.5f, transform.localPosition.z);
            float squash = 1f - bob * 0.6f;
            float stretch = 1f + bob * 0.4f;
            if (_flashRoutine == null) // don't fight the hit-squash coroutine
                _bodyRoot.localScale = new Vector3(_baseScale.x * stretch, _baseScale.y * squash, _baseScale.z * stretch);
        }

        /// <summary>Red flash + quick squash on taking damage — tier controls flash strength.</summary>
        public void HitFlash(Quality tier)
        {
            if (_defeated) return;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(HitFlashRoutine(tier));
        }

        IEnumerator HitFlashRoutine(Quality tier)
        {
            float strength = tier == Quality.Perfect ? 1f : (tier == Quality.Good ? 0.7f : 0.45f);
            SetEmissionAll(Color.red * strength);
            if (_bodyRoot != null) _bodyRoot.localScale = _baseScale * (1f - 0.18f * strength) + new Vector3(0f, 0.1f * strength, 0f);

            float t = 0f;
            const float dur = 0.22f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                SetEmissionAll(Color.Lerp(Color.red * strength, Color.black, k));
                if (_bodyRoot != null) _bodyRoot.localScale = Vector3.Lerp(_bodyRoot.localScale, _baseScale, k);
                yield return null;
            }
            RestoreBaseEmission();
            _flashRoutine = null;
        }

        /// <summary>Toggle the "incoming attack" glow pulse (enemy telegraph window).</summary>
        public void SetTelegraph(bool on)
        {
            if (_telegraphRoutine != null) { StopCoroutine(_telegraphRoutine); _telegraphRoutine = null; }
            if (on) _telegraphRoutine = StartCoroutine(TelegraphRoutine());
            else RestoreBaseEmission();
        }

        IEnumerator TelegraphRoutine()
        {
            float t = 0f;
            while (true)
            {
                t += Time.deltaTime * 3f;
                float pulse = (Mathf.Sin(t) + 1f) * 0.5f;
                SetEmissionAll(Color.Lerp(_tint * 0.2f, new Color(1f, 0.9f, 0.3f), pulse));
                yield return null;
            }
        }

        /// <summary>Shrink + spin + pop with a star-sparkle burst, then invokes onComplete.</summary>
        public void PlayDefeat(Action onComplete)
        {
            if (_defeated) return;
            _defeated = true;
            if (_telegraphRoutine != null) { StopCoroutine(_telegraphRoutine); _telegraphRoutine = null; }
            StartCoroutine(DefeatRoutine(onComplete));
        }

        IEnumerator DefeatRoutine(Action onComplete)
        {
            const float dur = 0.6f;
            float t = 0f;
            Vector3 start = _bodyRoot != null ? _bodyRoot.localScale : Vector3.one;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                if (_bodyRoot != null)
                {
                    _bodyRoot.localScale = Vector3.Lerp(start, Vector3.zero, k);
                    _bodyRoot.Rotate(Vector3.up, 720f * Time.deltaTime, Space.Self);
                }
                yield return null;
            }
            KinexFx.PopBurst(transform.position, new Color(1f, 0.85f, 0.25f), 40);
            Kinex.Sfx.Play("levelup", 0.8f);
            gameObject.SetActive(false);
            onComplete?.Invoke();
        }

        void SetEmissionAll(Color c)
        {
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var mat = r.material;
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", c);
                }
            }
        }

        void RestoreBaseEmission()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                var mat = r.material;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", _baseEmission[i]);
            }
        }
    }
}
