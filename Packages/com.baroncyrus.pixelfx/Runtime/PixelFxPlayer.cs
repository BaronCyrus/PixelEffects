using System;
using UnityEngine;
using UnityEngine.Events;

namespace PixelEffects
{
    /// <summary>
    /// Plays a <see cref="PixelFxAsset"/> on a SpriteRenderer: steps its frames at the effect's own rate, loops
    /// or ends, and tells you when it hits.
    /// </summary>
    [AddComponentMenu("PixelFX/PixelFX Player")]
    [RequireComponent(typeof(SpriteRenderer))]
    [DisallowMultipleComponent]
    public sealed class PixelFxPlayer : MonoBehaviour
    {
        public enum LoopMode
        {
            /// <summary>Loops play on a loop and one-shots play once, as the effect was made.</summary>
            Auto,
            /// <summary>Always repeat. A one-shot starts over, after <see cref="RepeatDelay"/>.</summary>
            Loop,
            /// <summary>Always play through once and end.</summary>
            Once,
        }

        public enum EndAction
        {
            /// <summary>Stay on the last frame.</summary>
            None,
            /// <summary>Turn the SpriteRenderer off.</summary>
            Hide,
            /// <summary>Deactivate the GameObject.</summary>
            Deactivate,
            /// <summary>Destroy the GameObject.</summary>
            Destroy,
        }

        [Serializable]
        public sealed class HitEvent : UnityEvent<PixelFxHit> { }

        static readonly int HueShiftId = Shader.PropertyToID("_HueShift");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] PixelFxAsset effect;

        [Tooltip("Start playing whenever the component is enabled.")]
        [SerializeField] bool playOnEnable = true;

        [Tooltip("Auto: as the effect was made. Loop: always repeat. Once: always play through and end.")]
        [SerializeField] LoopMode loopMode = LoopMode.Auto;

        [Tooltip("Playback speed. 1 is the effect's own frame rate.")]
        [SerializeField, Min(0f)] float speed = 1f;

        [Tooltip("Keep playing while Time.timeScale is 0 (menus, hit-stop done with time scale).")]
        [SerializeField] bool unscaledTime;

        [Tooltip("When a one-shot is set to loop: how long to rest between plays, in seconds.")]
        [SerializeField, Min(0f)] float repeatDelay;

        [Tooltip("What to do with the object when the effect ends.")]
        [SerializeField] EndAction whenFinished = EndAction.Hide;

        [Tooltip("Hold still on contact for as long as each hit says (the effect's own hit-stop).")]
        [SerializeField] bool hitStop;

        [Tooltip("Put the effect's material on the SpriteRenderer. The sheets are premultiplied with the glow baked in, so the default sprite material draws them wrong.")]
        [SerializeField] bool useEffectMaterial = true;

        [Tooltip("Turn every color round the wheel by this many degrees: a fire that burns blue, a heal that glows pink.")]
        [SerializeField, Range(-180f, 180f)] float hueShift;

        [Tooltip("Brightness. Above 1 pushes the glow into HDR, for bloom.")]
        [SerializeField, Min(0f)] float intensity = 1f;

        [Tooltip("Raised at each moment of contact in the effect.")]
        [SerializeField] HitEvent onHit = new HitEvent();

        [Tooltip("Raised when the effect ends (a loop ends when you stop it).")]
        [SerializeField] UnityEvent onFinished = new UnityEvent();

        SpriteRenderer rend;
        MaterialPropertyBlock block;
        float time, freezeLeft, waitLeft, fadeLeft = -1f, fadeTime, fadeFrom;
        int shown = -1, nextHit;
        bool playing, paused;

        /// <summary>Set by <see cref="PixelFx"/> on the instances it hands out: where to go back to when finished.</summary>
        internal Action<PixelFxPlayer> recycle;

        /// <summary>Raised at each moment of contact in the effect.</summary>
        public event Action<PixelFxPlayer, PixelFxHit> Hit;

        /// <summary>Raised when the effect ends (a loop ends when you stop it).</summary>
        public event Action<PixelFxPlayer> Finished;

        public HitEvent OnHit => onHit;
        public UnityEvent OnFinished => onFinished;

        /// <summary>The effect to play. Changing it while playing starts the new one from its first frame.</summary>
        public PixelFxAsset Effect
        {
            get => effect;
            set
            {
                if (effect == value)
                    return;
                effect = value;
                ApplyMaterial();
                if (playing)
                    Play();
                else
                    ShowPreview();
            }
        }

        public SpriteRenderer Renderer => rend != null ? rend : rend = GetComponent<SpriteRenderer>();

        public bool PlayOnEnable { get => playOnEnable; set => playOnEnable = value; }
        public LoopMode Loop { get => loopMode; set => loopMode = value; }
        public float Speed { get => speed; set => speed = Mathf.Max(0f, value); }
        public bool UnscaledTime { get => unscaledTime; set => unscaledTime = value; }
        public float RepeatDelay { get => repeatDelay; set => repeatDelay = Mathf.Max(0f, value); }
        public EndAction WhenFinished { get => whenFinished; set => whenFinished = value; }
        public bool HitStop { get => hitStop; set => hitStop = value; }

        public bool UseEffectMaterial
        {
            get => useEffectMaterial;
            set { useEffectMaterial = value; ApplyMaterial(); }
        }

        /// <summary>Degrees to turn every color round the wheel, -180 to 180.</summary>
        public float HueShift
        {
            get => hueShift;
            set { hueShift = value; ApplyBlock(); }
        }

        /// <summary>Brightness. Above 1 pushes the glow into HDR, for bloom.</summary>
        public float Intensity
        {
            get => intensity;
            set { intensity = Mathf.Max(0f, value); ApplyBlock(); }
        }

        public bool IsPlaying => playing;
        public bool IsPaused => paused;

        /// <summary>True while a hit is holding the effect still.</summary>
        public bool IsFrozen => freezeLeft > 0f;

        /// <summary>Seconds into the current play-through.</summary>
        public float PlaybackTime => time;

        /// <summary>The frame on show, or -1 when there is none.</summary>
        public int Frame => shown;

        /// <summary>Whether it repeats, given the effect and <see cref="Loop"/>.</summary>
        public bool IsLooping => effect != null && (loopMode == LoopMode.Loop || (loopMode == LoopMode.Auto && effect.loop));

        /// <summary>Play from the first frame.</summary>
        public void Play()
        {
            EndFade();
            time = 0f;
            freezeLeft = 0f;
            waitLeft = 0f;
            nextHit = 0;
            paused = false;
            shown = -1;
            if (effect == null || effect.FrameCount == 0)
            {
                playing = false;
                return;
            }
            ApplyMaterial();
            ApplyBlock();
            Renderer.enabled = true;
            playing = true;
            Show(0);
        }

        /// <summary>Play another effect from its first frame.</summary>
        public void Play(PixelFxAsset fx)
        {
            effect = fx;
            Play();
        }

        public void Pause() => paused = true;
        public void Resume() => paused = false;

        /// <summary>End now: raise Finished and do <see cref="WhenFinished"/>.</summary>
        public void Stop()
        {
            if (playing)
                Finish();
        }

        /// <summary>Let it end: fade out over a moment, then stop. The way to end a loop without a pop.</summary>
        public void FadeOut(float seconds = 0.3f)
        {
            if (!playing)
                return;
            if (seconds <= 0f)
            {
                Finish();
                return;
            }
            if (fadeLeft >= 0f)
                return;
            fadeTime = fadeLeft = seconds;
            fadeFrom = Renderer.color.a;
        }

        /// <summary>Jump to a time in the play-through, in seconds. Hits that were jumped over are not raised.</summary>
        public void Seek(float seconds)
        {
            if (effect == null || effect.FrameCount == 0)
                return;
            float length = effect.Duration;
            time = IsLooping && length > 0f ? Mathf.Repeat(seconds, length) : Mathf.Clamp(seconds, 0f, length);
            freezeLeft = 0f;
            waitLeft = 0f;
            nextHit = 0;
            var hits = effect.hits;
            while (hits != null && nextHit < hits.Length && hits[nextHit].time < time)
                nextHit++;
            Show(effect.FrameAt(time, false));
        }

        /// <summary>
        /// Move playback on by this much time, in seconds. Update calls it every frame; call it yourself to
        /// drive an effect from your own clock (turn the component's Update off by pausing it first).
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!playing || effect == null || deltaTime <= 0f)
                return;

            if (fadeLeft >= 0f)
            {
                fadeLeft -= deltaTime;
                if (fadeLeft <= 0f)
                {
                    Finish();
                    return;
                }
                SetAlpha(fadeFrom * (fadeLeft / fadeTime));
            }

            // a hit holds everything still
            if (freezeLeft > 0f)
            {
                freezeLeft -= deltaTime;
                if (freezeLeft > 0f)
                    return;
                deltaTime = -freezeLeft;
                freezeLeft = 0f;
            }

            // a one-shot on repeat rests between plays
            if (waitLeft > 0f)
            {
                waitLeft -= deltaTime;
                if (waitLeft > 0f)
                    return;
                deltaTime = -waitLeft;
                waitLeft = 0f;
                time = 0f;
                nextHit = 0;
            }

            float length = effect.Duration;
            time += deltaTime;
            if (!RaiseHits())
                return;

            if (freezeLeft <= 0f && time >= length)
            {
                if (!IsLooping)
                {
                    Show(effect.FrameCount - 1);
                    Finish();
                    return;
                }
                if (!effect.loop && repeatDelay > 0f)
                {
                    waitLeft = repeatDelay;
                    shown = -1;
                    Renderer.sprite = null;
                    return;
                }
                time = length > 0f ? time % length : 0f;
                nextHit = 0;
                if (!RaiseHits())
                    return;
            }

            Show(effect.FrameAt(time, false));
        }

        // Raise the hits playback has reached. False when a listener stopped or restarted the effect.
        bool RaiseHits()
        {
            var fx = effect;
            var hits = fx.hits;
            while (hits != null && nextHit < hits.Length && hits[nextHit].time <= time)
            {
                var hit = hits[nextHit++];
                float at = time;
                Hit?.Invoke(this, hit);
                onHit.Invoke(hit);
                if (!playing || effect != fx || time < at)
                    return false;
                if (hitStop && hit.freeze > 0f)
                {
                    time = hit.time;
                    freezeLeft = hit.freeze;
                    break;
                }
            }
            return true;
        }

        void Finish()
        {
            bool faded = fadeLeft >= 0f || fadeTime > 0f;
            playing = false;
            paused = false;
            freezeLeft = 0f;
            waitLeft = 0f;
            if (faded)
            {
                // it faded to nothing: leave nothing on show, and give the renderer its alpha back
                shown = -1;
                Renderer.sprite = null;
                EndFade();
            }

            Finished?.Invoke(this);
            onFinished.Invoke();
            if (playing || this == null)
                return; // a listener started it again, or destroyed it

            if (recycle != null)
            {
                recycle(this);
                return;
            }
            switch (whenFinished)
            {
                case EndAction.Hide:
                    Renderer.enabled = false;
                    break;
                case EndAction.Deactivate:
                    gameObject.SetActive(false);
                    break;
                case EndAction.Destroy:
                    Destroy(gameObject);
                    break;
            }
        }

        void Show(int frame)
        {
            if (frame == shown)
                return;
            shown = frame;
            Renderer.sprite = effect != null ? effect.GetFrame(frame) : null;
        }

        void ShowPreview()
        {
            if (effect == null)
                return;
            shown = -1;
            var sprite = effect.PreviewSprite;
            if (Renderer.sprite != sprite)
                Renderer.sprite = sprite;
        }

        void SetAlpha(float a)
        {
            var c = Renderer.color;
            c.a = a;
            Renderer.color = c;
        }

        void EndFade()
        {
            if (fadeTime > 0f)
                SetAlpha(fadeFrom);
            fadeLeft = -1f;
            fadeTime = 0f;
        }

        void ApplyMaterial()
        {
            if (!useEffectMaterial || effect == null || effect.material == null)
                return;
            if (Renderer.sharedMaterial != effect.material)
                Renderer.sharedMaterial = effect.material;
        }

        void ApplyBlock()
        {
            bool custom = !Mathf.Approximately(hueShift, 0f) || !Mathf.Approximately(intensity, 1f);
            // leave the renderer without a block (so it can batch) until something asks for one
            if (!custom && !Renderer.HasPropertyBlock())
                return;
            if (block == null)
                block = new MaterialPropertyBlock();
            Renderer.GetPropertyBlock(block);
            block.SetFloat(HueShiftId, hueShift);
            block.SetFloat(IntensityId, intensity);
            Renderer.SetPropertyBlock(block);
        }

        void OnEnable()
        {
            ApplyMaterial();
            ApplyBlock();
            if (playOnEnable)
                Play();
        }

        void OnDisable()
        {
            // a disabled player is not playing; enabling it again starts over (or stays put, without Play On Enable)
            EndFade();
            playing = false;
            paused = false;
        }

        void Update()
        {
            if (!playing || paused)
                return;
            Tick((unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) * speed);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // not allowed to touch other components from inside OnValidate, so do it right after
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null)
                    return;
                ApplyMaterial();
                ApplyBlock();
                if (!Application.isPlaying)
                    ShowPreview();
            };
        }
#endif
    }
}
