using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DestroyOnAudioFinishPlaying : MonoBehaviour
{
    private static readonly List<float> RecentImpactTimes = new List<float>();

    [SerializeField, Range(0f, 1f)] private float baseVolume = 0.025f;
    [SerializeField, Range(0.1f, 3f)] private float basePitch = 0.72f;
    [SerializeField, Range(0f, 0.5f)] private float pitchRandomRange = 0.06f;
    [SerializeField, Min(0.01f)] private float burstWindowSeconds = 0.35f;
    [SerializeField, Range(0.1f, 1f)] private float burstVolumeMultiplier = 0.68f;
    [SerializeField, Min(0f)] private float nearDuplicateWindowSeconds = 0.04f;

    private AudioSource source;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        TuneImpactSound();
    }

    private void Start()
    {
        StartCoroutine(WaitCoroutine());
    }

    IEnumerator WaitCoroutine()
    {
        if (source == null || source.clip == null)
        {
            Destroy(gameObject);
            yield break;
        }

        yield return new WaitForSeconds(source.clip.length / Mathf.Max(0.01f, source.pitch));
        Destroy(gameObject);
    }

    private void TuneImpactSound()
    {
        if (source == null)
        {
            return;
        }

        float now = Time.unscaledTime;
        for (int i = RecentImpactTimes.Count - 1; i >= 0; i--)
        {
            if (now - RecentImpactTimes[i] > burstWindowSeconds)
            {
                RecentImpactTimes.RemoveAt(i);
            }
        }

        bool nearDuplicate = false;
        for (int i = 0; i < RecentImpactTimes.Count; i++)
        {
            if (now - RecentImpactTimes[i] <= nearDuplicateWindowSeconds)
            {
                nearDuplicate = true;
                break;
            }
        }

        float volume = baseVolume * Mathf.Pow(burstVolumeMultiplier, RecentImpactTimes.Count);
        if (nearDuplicate)
        {
            volume *= 0.35f;
        }

        source.volume = Mathf.Clamp01(volume);
        source.pitch = basePitch + Random.Range(-pitchRandomRange, pitchRandomRange);
        source.loop = false;
        source.playOnAwake = true;
        source.spatialBlend = 0f;

        RecentImpactTimes.Add(now);
    }
}
