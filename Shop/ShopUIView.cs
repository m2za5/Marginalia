using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class ShopSlotView
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text priceLabel;

    public Button Button => button;
    public Image Icon => icon;
    public TMP_Text Label => label;
    public TMP_Text PriceLabel => priceLabel;
}

[DisallowMultipleComponent]
public sealed class ShopUIView : MonoBehaviour
{
    [Header("Canvas")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text currencyText;
    [SerializeField] private Button closeButton;

    [Header("Item Slots")]
    [SerializeField] private ShopSlotView[] slots = Array.Empty<ShopSlotView>();

    [Header("View State")]
    [SerializeField] private GameObject listPanel;
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private Button backButton;

    [Header("Preview")]
    [SerializeField] private Image previewIcon;
    [SerializeField] private TMP_Text previewNameText;
    [SerializeField] private TMP_Text previewDescriptionText;
    [SerializeField] private TMP_Text previewPriceText;
    [SerializeField] private Button buyButton;

    [Header("Feedback")]
    [SerializeField] private TMP_Text messageText;

    [Header("Selection Colors")]
    [SerializeField] private Color normalSlotColor = Color.clear;
    [SerializeField] private Color selectedColor = new Color(0.73f, 0.52f, 0.2f, 0.22f);
    [SerializeField] private Color soldOutColor = new Color(0.35f, 0.35f, 0.35f, 0.35f);

    public Canvas Canvas => canvas;
    public CanvasGroup CanvasGroup => canvasGroup;
    public TMP_Text TitleText => titleText;
    public TMP_Text CurrencyText => currencyText;
    public Button CloseButton => closeButton;
    public IReadOnlyList<ShopSlotView> Slots => slots;
    public Button BackButton => backButton;
    public Image PreviewIcon => previewIcon;
    public TMP_Text PreviewNameText => previewNameText;
    public TMP_Text PreviewDescriptionText => previewDescriptionText;
    public TMP_Text PreviewPriceText => previewPriceText;
    public Button BuyButton => buyButton;
    public TMP_Text MessageText => messageText;
    public Color NormalSlotColor => normalSlotColor;
    public Color SelectedColor => selectedColor;
    public Color SoldOutColor => soldOutColor;

    public void ShowList()
    {
        if (listPanel != null)
        {
            listPanel.SetActive(true);
        }

        if (detailPanel != null)
        {
            detailPanel.SetActive(false);
        }
    }

    public void ShowDetail()
    {
        if (listPanel != null)
        {
            listPanel.SetActive(false);
        }

        if (detailPanel != null)
        {
            detailPanel.SetActive(true);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        if (canvas == null)
            Debug.LogError($"[ShopUIView] {name}: Canvas 미할당. 상점을 열고 닫을 수 X", this);

        if (canvasGroup == null)
            Debug.LogError($"[ShopUIView] {name}: CanvasGroup 미할당. 클릭 차단이 동작X.", this);

        if (slots == null || slots.Length == 0)
            Debug.LogWarning($"[ShopUIView] {name}: 슬롯X. 상품이 표시X.", this);
    }
#endif
}
