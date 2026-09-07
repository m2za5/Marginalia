using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class DialogueBlurController : MonoBehaviour
{
    [SerializeField] private DialogueManager manager;
    [SerializeField] private Volume blurVolume;
    [SerializeField] private float fadeInDuration = 0.4f;
    [SerializeField] private float fadeOutDuration = 0.3f;
    [Range(0f, 1f)]
    [SerializeField] private float targetWeight = 1f;
    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        if (blurVolume != null) blurVolume.weight = 0f;
    }
    private void OnEnable()
    {
        if (manager == null || blurVolume == null)
        {
            Debug.LogError("[DialogueBlurController] manager/blurVolume 참조 비어있음");
            return;
        }
        manager.OnDialogueStarted += FadeIn;
        manager.OnDialogueEnded += FadeOut;
    }
    private void OnDisable()
    {
        if (manager == null) return;
        manager.OnDialogueStarted -= FadeIn;
        manager.OnDialogueEnded -= FadeOut;
    }
    private void FadeIn() => StartFade(targetWeight, fadeInDuration);
    private void FadeOut() => StartFade(0f, fadeOutDuration);
    private void StartFade(float target, float duration)
    {
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeRoutine(target, duration));
    }
    private IEnumerator FadeRoutine(float target, float duration)
    {
        float start = blurVolume.weight;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            blurVolume.weight = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }
        blurVolume.weight = target;
    }
}