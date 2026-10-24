using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Forwards clicks from either collider on a block to the game manager.</summary>
public sealed class BlockClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private GameObject obj;

    private GameObject blockRoot;

    public GameObject BlockRoot => blockRoot;

    private void Awake()
    {
        // The prefab has separate hit areas for the covered and flagged faces.
        // Their serialized target is a direct child of the block root.
        if (obj != null && obj.transform.parent != null)
            blockRoot = obj.transform.parent.gameObject;
        else
        {
            Transform current = transform;
            while (current.parent != null && current.parent.name.StartsWith("Block"))
                current = current.parent;
            blockRoot = current.gameObject;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            HandleClick(false);
        else if (eventData.button == PointerEventData.InputButton.Right)
            HandleClick(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetPointerState(true, false);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetPointerState(false, false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            SetPointerState(true, true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            SetPointerState(eventData.pointerEnter == gameObject, false);
    }

    private void SetPointerState(bool hovered, bool pressed)
    {
        if (blockRoot != null && GameManager.Instance != null)
            GameManager.Instance.SetBlockPointerState(blockRoot, hovered, pressed);
    }

    private void HandleClick(bool alternate)
    {
        if (blockRoot == null) return;

        if (GameManager.Instance != null)
            GameManager.Instance.ClickBlock(blockRoot, alternate);
    }
}
