using UnityEngine;

// Ersetzt das harte SetActive(false) der Tiles durch eine lesbare Animation:
// erst wackeln (Vorwarnung), dann Collider sofort aus und die Platte kippt weg.
// Wird von ColorDashManager zur Laufzeit auf jedes Tile gelegt - keine Szenenarbeit nötig.
[DisallowMultipleComponent]
public class TileAnimator : MonoBehaviour
{
    private enum State { Idle, Wobble, Dropping, Hidden }

    [Header("Wackeln")]
    public float wobbleAmplitude = 0.05f;
    public float wobbleSpeed = 26f;
    public float wobbleTilt = 3.5f;

    [Header("Herunterfallen")]
    public float dropGravity = 34f;
    public float hideAfterDistance = 16f;

    private State state = State.Idle;

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Renderer[] renderers;
    private Collider[] colliders;

    private float wobblePhase;
    private float dropDelay;
    private float dropTimer;
    private float fallVelocity;
    private Vector3 spinAxis;
    private float spinSpeed;

    void Awake()
    {
        homePosition = transform.localPosition;
        homeRotation = transform.localRotation;
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        wobblePhase = Random.value * 20f;
    }

    public void SetWobble(bool on)
    {
        if (state == State.Dropping || state == State.Hidden) return;

        if (on)
        {
            state = State.Wobble;
        }
        else if (state == State.Wobble)
        {
            state = State.Idle;
            ResetTransform();
        }
    }

    // delay verzögert nur die Optik. Die Collider gehen sofort aus, damit alle Clients
    // im selben Moment durchfallen und die Kaskade rein kosmetisch bleibt.
    public void Drop(float delay)
    {
        if (state == State.Dropping || state == State.Hidden) return;

        state = State.Dropping;
        dropDelay = Mathf.Max(0f, delay);
        dropTimer = 0f;
        fallVelocity = 0f;
        spinAxis = Random.onUnitSphere;
        spinSpeed = Random.Range(40f, 150f);

        SetCollidersEnabled(false);
    }

    public void Restore()
    {
        state = State.Idle;
        ResetTransform();
        SetRenderersEnabled(true);
        SetCollidersEnabled(true);
    }

    void Update()
    {
        switch (state)
        {
            case State.Wobble:
                float t = (Time.time + wobblePhase) * wobbleSpeed;
                transform.localPosition = homePosition + Vector3.up * (Mathf.Sin(t) * wobbleAmplitude);
                transform.localRotation = homeRotation * Quaternion.Euler(
                    Mathf.Sin(t * 0.9f) * wobbleTilt, 0f, Mathf.Cos(t * 1.1f) * wobbleTilt);
                break;

            case State.Dropping:
                dropTimer += Time.deltaTime;
                if (dropTimer < dropDelay) break;

                fallVelocity += dropGravity * Time.deltaTime;
                transform.localPosition += Vector3.down * (fallVelocity * Time.deltaTime);
                transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.Self);

                if (homePosition.y - transform.localPosition.y > hideAfterDistance)
                {
                    state = State.Hidden;
                    SetRenderersEnabled(false);
                }
                break;
        }
    }

    private void ResetTransform()
    {
        transform.localPosition = homePosition;
        transform.localRotation = homeRotation;
    }

    private void SetRenderersEnabled(bool enabled)
    {
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = enabled;
    }

    private void SetCollidersEnabled(bool enabled)
    {
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = enabled;
    }
}
