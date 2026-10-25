using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DamageInsight.UI;

/// <summary>
/// Holding the left mouse button on an object for a few seconds (V013_SPEC part C: a hidden achievement's veil,
/// Umut's 3-5 s, 4 [DECISION]): reports how far along it is (0..1, back to 0 when let go or left) and calls
/// <see cref="Done"/> once it is full. Unscaled time (the game is paused while the book is open).
/// </summary>
public sealed class HoldToReveal : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public float Seconds = 4f;
    public Action<float> Progress;
    public Action Done;
    /// <summary>Off: holding does nothing (the object isn't veiled).</summary>
    public bool Armed;

    private bool _held;
    private float _time;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!Armed || eventData.button != PointerEventData.InputButton.Left)
            return;
        _held = true;
        _time = 0f;
    }

    public void OnPointerUp(PointerEventData eventData) => Stop();

    public void OnPointerExit(PointerEventData eventData) => Stop();

    private void OnDisable() => Stop();

    private void Stop()
    {
        if (!_held && _time == 0f)
            return;
        _held = false;
        _time = 0f;
        Progress?.Invoke(0f);
    }

    private void Update()
    {
        if (!_held)
            return;
        if (!Armed)
        {
            Stop();
            return;
        }
        _time += Time.unscaledDeltaTime;
        if (_time < Seconds)
        {
            Progress?.Invoke(_time / Seconds);
            return;
        }
        _held = false;
        _time = 0f;
        Progress?.Invoke(0f);
        Done?.Invoke();
    }
}
