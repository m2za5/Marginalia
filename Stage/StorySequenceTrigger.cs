using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class StorySequenceTrigger : MonoBehaviour
{
    [SerializeField] private StorySequenceRunner runner;
    [SerializeField] private bool playOnce = true;

    private void Awake()
    {
        if (runner == null || !GetComponent<BoxCollider>().isTrigger)
        {
            Debug.LogError("[StorySequenceTrigger] Assign a Runner and enable BoxCollider.IsTrigger.", this);
            enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (runner == null || runner.IsRunning || (playOnce && runner.HasCompleted))
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null) runner.TryBegin(player);
    }
}
