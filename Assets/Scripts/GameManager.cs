using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using TMPro;

/// <summary>Owns the board, rules, timer, and in-game controls.</summary>
public class GameManager : Singleton<GameManager>
{
    [Header("Board")]
    [SerializeField, Min(1)] private int mineCount = 10;
    [SerializeField] private int randomSeed;
    [SerializeField, Range(5, 25)] private int gridSize = 9;

    private sealed class Cell
    {
        public GameObject root;
        public GameObject coveredFace;
        public GameObject flagFace;
        public SpriteRenderer coveredRenderer;
        public Vector3 baseScale;
        public int column;
        public int row;
        public int adjacentMines;
        public bool isMine;
        public bool isRevealed;
        public bool isFlagged;
        public bool belongsToBoard;
        public bool isHovered;
        public bool isPressed;
        public bool isHinted;
    }

    private readonly List<Cell> cells = new List<Cell>();
    private readonly Dictionary<GameObject, Cell> cellByRoot = new Dictionary<GameObject, Cell>();
    private Cell[,] boardGrid;
    private int columns;
    private int rows;
    private int flagsPlaced;
    private float elapsed;
    private bool timerStarted;
    private bool firstReveal = true;
    private bool gameEnded;
    private bool gameStarted;
    private bool won;
    private bool flagMode;
    private string statusMessage = "Choose a tile to begin.";
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle cellNumberStyle;
    private GUIStyle statusStyle;
    private GUIStyle hudBoxStyle;
    private GUIStyle hudButtonStyle;
    private GUIStyle overlayStyle;
    private Texture2D panelTexture;
    private Texture2D buttonTexture;
    private Texture2D accentTexture;
    private int lastClickFrame = -1;
    private Button weaponButton;
    private Button openModeButton;
    private Button flagModeButton;
    private Button hintButton;
    private Button runtimeResetButton;
    private Canvas runtimeCanvas;
    private int lastRuntimeScreenWidth = -1;
    private int lastRuntimeScreenHeight = -1;
    private Canvas setupCanvas;
    private TMP_Text setupGridLabel;
    private TMP_Text setupMineLabel;
    private TMP_Text setupEnvironmentLabel;
    private Transform boardParent;
    private GameObject blockTemplate;
    private Transform boardBackdrop;
    private Camera sceneCamera;
    private Vector3 initialCameraPosition;
    private float initialCameraOrthographicSize;
    private Vector3 backdropBaseScale;
    private Sprite defaultBackdropSprite;
    private Sprite grassBackground;
    private Sprite seaBackground;
    private Sprite volcanoBackground;
    private Sprite grassAtmosphere;
    private Sprite seaAtmosphere;
    private Sprite volcanoAtmosphere;
    private SpriteRenderer atmosphereRenderer;
    private float backdropPaddingWorld = 0.5f;

    private void Start()
    {
        BuildBoardFromScene();
        foreach (var cell in cells) cell.root.SetActive(false);
        BindWeaponButton();
        BuildSetupUI();
        BuildRuntimeUI();
    }

    private void Update()
    {
        if (timerStarted && !gameEnded) elapsed += Time.deltaTime;
        if (gameStarted && Keyboard.current != null)
        {
            if (Keyboard.current.fKey.wasPressedThisFrame) flagMode = !flagMode;
            if (Keyboard.current.rKey.wasPressedThisFrame) ReturnToSetup();
        }
        RefreshRuntimeButtonLayout();
        FitThemeAtmosphere();
    }

    private void BuildBoardFromScene()
    {
        var camera = Camera.main;
        sceneCamera = camera;
        if (sceneCamera != null)
        {
            initialCameraPosition = sceneCamera.transform.position;
            initialCameraOrthographicSize = sceneCamera.orthographic ? sceneCamera.orthographicSize : 0f;
        }
        if (camera != null && camera.GetComponent<Physics2DRaycaster>() == null)
            camera.gameObject.AddComponent<Physics2DRaycaster>();

        cells.Clear();
        cellByRoot.Clear();
        var clicks = FindObjectsByType<BlockClick>(FindObjectsInactive.Include);
        foreach (var click in clicks)
        {
            GameObject root = click.BlockRoot;
            if (root == null || cellByRoot.ContainsKey(root)) continue;
            var cell = new Cell
            {
                root = root,
                coveredFace = root.transform.Find("BlockFront")?.gameObject,
                flagFace = root.transform.Find("BlockFlag")?.gameObject,
                coveredRenderer = root.transform.Find("BlockFront")?.GetComponentInChildren<SpriteRenderer>(),
                baseScale = root.transform.localScale
            };
            cellByRoot.Add(root, cell);
            cells.Add(cell);
        }

        if (cells.Count == 0)
        {
            Debug.LogError("GameManager could not find any BlockClick components. Add the Block prefab to the active scene.");
            return;
        }

        // Block roots are children of a board transform whose world position
        // includes a half-cell vertical offset. Index local coordinates so
        // rounding cannot merge neighboring rows into the same grid row.
        var xs = cells.Select(c => Mathf.RoundToInt(c.root.transform.localPosition.x * 1000f)).Distinct().OrderBy(x => x).ToList();
        var ys = cells.Select(c => Mathf.RoundToInt(c.root.transform.localPosition.y * 1000f)).Distinct().OrderBy(y => y).ToList();
        columns = xs.Count;
        rows = ys.Count;
        foreach (var cell in cells)
        {
            cell.column = xs.IndexOf(Mathf.RoundToInt(cell.root.transform.localPosition.x * 1000f));
            cell.row = ys.IndexOf(Mathf.RoundToInt(cell.root.transform.localPosition.y * 1000f));
        }
        cells.Sort((a, b) => a.row != b.row ? a.row.CompareTo(b.row) : a.column.CompareTo(b.column));
        gridSize = Mathf.Clamp(gridSize, 5, 25);
        ClampMineCount();
        if (cells.Count > 0)
        {
            boardParent = cells[0].root.transform.parent;
            blockTemplate = cells[0].root;
        }
        var backdropObject = GameObject.Find("BackGround");
        if (backdropObject != null)
        {
            boardBackdrop = backdropObject.transform;
            backdropBaseScale = boardBackdrop.localScale;
            var renderer = boardBackdrop.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                defaultBackdropSprite = renderer.sprite;
                float cellWorldWidth = boardParent != null ? Mathf.Abs(boardParent.lossyScale.x) : 1f;
                float initialWorldWidth = renderer.sprite != null ? renderer.sprite.bounds.size.x * Mathf.Abs(backdropBaseScale.x) : 6.4f;
                backdropPaddingWorld = Mathf.Max(0.5f, initialWorldWidth - 9f * cellWorldWidth);
            }
            grassBackground = LoadBackground("GrassBackground");
            seaBackground = LoadBackground("SeaBackground");
            volcanoBackground = LoadBackground("VolcanoBackground");
            grassAtmosphere = LoadBackground("GrassAtmosphere");
            seaAtmosphere = LoadBackground("SeaAtmosphere");
            volcanoAtmosphere = LoadBackground("VolcanoAtmosphere");
            CreateThemeAtmosphere();
            UpdateBackgroundForBoardSize();
        }
    }

    public void ClickBlock(GameObject blockRoot, bool alternate)
    {
        // The prefab has overlapping hit areas on its covered and flagged faces.
        if (lastClickFrame == Time.frameCount) return;
        lastClickFrame = Time.frameCount;
        if (gameEnded || !cellByRoot.TryGetValue(blockRoot, out var cell)) return;
        ClearHint(cell);

        bool placeFlag = alternate || flagMode;
        if (placeFlag)
        {
            ToggleFlag(cell);
            return;
        }
        if (cell.isFlagged || cell.isRevealed) return;

        if (firstReveal)
        {
            PlaceMines(cell);
            firstReveal = false;
            timerStarted = true;
        }

        if (cell.isMine)
        {
            SpawnBurst(cell.root.transform.position, new Color(1f, 0.24f, 0.18f), 44);
            RevealAllMines();
            gameEnded = true;
            won = false;
            overlayStartedAt = Time.unscaledTime;
            statusMessage = "Mine hit! Press Restart to try again.";
            return;
        }

        SpawnBurst(cell.root.transform.position, new Color(0.22f, 0.86f, 1f), 12);
        Reveal(cell);
        CheckForWin();
    }

    public void SetBlockPointerState(GameObject blockRoot, bool hovered, bool pressed)
    {
        if (!gameStarted || gameEnded || !cellByRoot.TryGetValue(blockRoot, out var cell) || cell.isRevealed) return;
        cell.isHovered = hovered;
        cell.isPressed = pressed;
        if (cell.coveredRenderer != null)
            cell.coveredRenderer.color = pressed
                ? new Color(0.52f, 0.88f, 1f, 1f)
                : hovered ? new Color(0.76f, 0.94f, 1f, 1f)
                : cell.isHinted ? new Color(0.68f, 1f, 0.42f, 1f) : Color.white;
        cell.root.transform.localScale = cell.baseScale * (pressed ? 0.93f : 1f);
    }

    private void ToggleFlag(Cell cell)
    {
        if (cell.isRevealed) return;
        if (!cell.isFlagged && flagsPlaced >= mineCount) return;
        cell.isFlagged = !cell.isFlagged;
        ClearHint(cell);
        flagsPlaced += cell.isFlagged ? 1 : -1;
        if (cell.flagFace != null) cell.flagFace.SetActive(cell.isFlagged);
        if (cell.isFlagged && cell.flagFace != null) StartCoroutine(PopTransform(cell.flagFace.transform));
        SpawnBurst(cell.root.transform.position, cell.isFlagged ? new Color(1f, 0.45f, 0.54f) : new Color(0.38f, 0.87f, 0.95f), 8);
    }

    private void PlaceMines(Cell firstCell)
    {
        int seed = randomSeed == 0 ? System.Environment.TickCount : randomSeed;
        var rng = new System.Random(seed);
        // Keep the first click and its surrounding 3x3 area safe.
        var candidates = cells.Where(c => c.belongsToBoard &&
            !c.isHinted && (Mathf.Abs(c.column - firstCell.column) > 1 || Mathf.Abs(c.row - firstCell.row) > 1)).ToList();

        for (int i = 0; i < mineCount; i++)
        {
            int selected = rng.Next(i, candidates.Count);
            (candidates[i], candidates[selected]) = (candidates[selected], candidates[i]);
            candidates[i].isMine = true;
        }

        RefreshAdjacentMineCounts();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int placedMineCount = cells.Count(c => c.belongsToBoard && c.isMine);
        if (placedMineCount != mineCount)
            Debug.LogError($"Mine placement mismatch: expected {mineCount}, placed {placedMineCount}.");
#endif
        statusMessage = "First 3×3 is safe. Tap FLAG to mark a mine.";
    }

    private List<Cell> GetNeighbors(Cell cell)
    {
        var neighbors = new List<Cell>(8);
        if (boardGrid == null) return neighbors;
        for (int row = Mathf.Max(0, cell.row - 1); row <= Mathf.Min(rows - 1, cell.row + 1); row++)
        {
            for (int column = Mathf.Max(0, cell.column - 1); column <= Mathf.Min(columns - 1, cell.column + 1); column++)
            {
                if (column == cell.column && row == cell.row) continue;
                Cell neighbor = boardGrid[column, row];
                if (neighbor != null) neighbors.Add(neighbor);
            }
        }
        return neighbors;
    }

    private int CountAdjacentMines(Cell cell)
    {
        int count = 0;
        foreach (var neighbor in GetNeighbors(cell))
            if (neighbor.isMine) count++;
        return count;
    }

    private void RefreshAdjacentMineCounts()
    {
        foreach (var cell in cells)
            if (cell.belongsToBoard) cell.adjacentMines = CountAdjacentMines(cell);
    }

    private void Reveal(Cell start)
    {
        var pending = new Queue<Cell>();
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            Cell cell = pending.Dequeue();
            if (cell.isRevealed || cell.isFlagged || cell.isMine) continue;
            cell.isRevealed = true;
            if (cell.coveredFace != null) cell.coveredFace.SetActive(false);
            StartCoroutine(PopTransform(cell.root.transform));
            if (CountAdjacentMines(cell) == 0)
                foreach (var neighbor in GetNeighbors(cell))
                    if (!neighbor.isRevealed && !neighbor.isFlagged && !neighbor.isMine) pending.Enqueue(neighbor);
        }
    }

    private void RevealAllMines()
    {
        foreach (var cell in cells)
        {
            if (!cell.isMine) continue;
            cell.isRevealed = true;
            if (cell.coveredFace != null) cell.coveredFace.SetActive(false);
        }
    }

    private void CheckForWin()
    {
        if (cells.Any(c => c.belongsToBoard && !c.isMine && !c.isRevealed)) return;
        gameEnded = true;
        won = true;
        overlayStartedAt = Time.unscaledTime;
        SpawnWinBursts();
        statusMessage = "Board cleared! Great job.";
        timerStarted = false;
    }

    private void ResetGame()
    {
        flagsPlaced = 0;
        elapsed = 0f;
        timerStarted = false;
        firstReveal = true;
        gameEnded = false;
        won = false;
        flagMode = false;
        statusMessage = "Choose a tile to begin.";
        foreach (var cell in cells)
        {
            cell.root.SetActive(gameStarted && cell.belongsToBoard);
            cell.root.transform.localScale = cell.baseScale;
            cell.isHovered = false;
            cell.isPressed = false;
            cell.isHinted = false;
            if (cell.coveredRenderer != null) cell.coveredRenderer.color = Color.white;
            cell.isMine = false;
            cell.isRevealed = false;
            cell.isFlagged = false;
            cell.adjacentMines = 0;
            if (cell.coveredFace != null) cell.coveredFace.SetActive(true);
            if (cell.flagFace != null) cell.flagFace.SetActive(false);
        }
        if (weaponButton != null) weaponButton.gameObject.SetActive(false);
        if (setupCanvas != null) setupCanvas.gameObject.SetActive(!gameStarted);
        if (runtimeCanvas != null) runtimeCanvas.gameObject.SetActive(gameStarted);
        UpdateRuntimeButtonState();
    }

    private void ReturnToSetup()
    {
        if (!gameStarted) return;
        gameStarted = false;
        ResetGame();
        if (sceneCamera != null && sceneCamera.orthographic && initialCameraOrthographicSize > 0f)
        {
            sceneCamera.transform.position = initialCameraPosition;
            sceneCamera.orthographicSize = initialCameraOrthographicSize;
        }
        RefreshSetupLabels();
        UpdateBackgroundForBoardSize();
        FitThemeAtmosphere();
    }

    private void OnGUI()
    {
        EnsureStyles();
        if (!gameStarted)
            return;
        float panelWidth = Mathf.Min(620f, Screen.width - 32f);
        float uiScale = panelWidth / 620f;
        GUI.Box(new Rect((Screen.width - panelWidth) * 0.5f, 14f, panelWidth, 92f), GUIContent.none, hudBoxStyle);
        float left = (Screen.width - panelWidth) * 0.5f;
        GUI.DrawTexture(new Rect(left + 16f * uiScale, 28f, 4f * uiScale, 62f), accentTexture);
        GUI.Label(new Rect(left + 30f * uiScale, 24f, 206f * uiScale, 32f), "MINE / SWEEPER", headerStyle);
        GUI.Label(new Rect(left + 30f * uiScale, 58f, 206f * uiScale, 24f), statusMessage, statusStyle);
        GUI.Label(new Rect(left + 250f * uiScale, 27f, 112f * uiScale, 42f), "MINES\n" + Mathf.Max(0, mineCount - flagsPlaced).ToString("000"), labelStyle);
        GUI.Label(new Rect(left + 365f * uiScale, 27f, 94f * uiScale, 42f), "TIME\n" + Mathf.FloorToInt(elapsed).ToString("000"), labelStyle);
        if (gameEnded)
        {
            float alpha = Mathf.Clamp01((Time.unscaledTime - overlayStartedAt) / 0.35f);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            var overlay = new Rect((Screen.width - 360) / 2f, Screen.height - 96, 360, 64);
            GUI.Box(overlay, won ? "BOARD CLEARED  ✦" : "MINE DETONATED", overlayStyle);
            GUI.color = Color.white;
        }

        DrawNumbers();
    }

    private void BuildSetupUI()
    {
        var canvasObject = new GameObject("Game Setup Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        setupCanvas = canvasObject.GetComponent<Canvas>();
        setupCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        setupCanvas.overrideSorting = true;
        setupCanvas.sortingOrder = 50;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(960f, 600f);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = CreatePanel(canvasObject.transform, "Setup Panel", Vector2.zero, new Vector2(520f, 390f), new Color(0.035f, 0.055f, 0.09f, 0.97f));
        var panelImage = panel.GetComponent<Image>();
        panelImage.raycastTarget = false;
        panelImage.color = new Color(0.035f, 0.055f, 0.09f, 0.97f);
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.2f, 0.78f, 0.88f, 0.65f);
        outline.effectDistance = new Vector2(1f, -1f);
        CreateSetupText(panel, "Eyebrow", "FIELD OPERATIONS  /  01", 12, new Vector2(0f, 164f), new Vector2(440f, 22f), FontStyles.Bold, new Color(0.25f, 0.86f, 0.9f));
        CreateSetupText(panel, "Title", "MINE SWEEPER", 32, new Vector2(0f, 127f), new Vector2(440f, 48f), FontStyles.Bold, new Color(0.93f, 0.97f, 1f));
        CreateSetupText(panel, "Description", "Choose a 5×5–25×25 board · mines are 10–40% · first 3×3 is safe", 14, new Vector2(0f, 94f), new Vector2(470f, 30f), FontStyles.Normal, Color.white);
        CreateSetupText(panel, "Board Label", "BOARD SIZE", 15, new Vector2(-150f, 34f), new Vector2(150f, 40f), FontStyles.Bold, new Color(0.63f, 0.72f, 0.8f));
        CreateSetupButton(panel, "Board Minus", "−", new Vector2(-40f, 34f), new Vector2(54f, 44f), () => ChangeGridSize(-1));
        setupGridLabel = CreateSetupText(panel, "Board Value", "", 22, new Vector2(40f, 34f), new Vector2(100f, 44f), FontStyles.Bold, Color.white);
        CreateSetupButton(panel, "Board Plus", "+", new Vector2(120f, 34f), new Vector2(54f, 44f), () => ChangeGridSize(1));
        CreateSetupText(panel, "Bomb Label", "MINE COUNT", 15, new Vector2(-150f, -38f), new Vector2(150f, 40f), FontStyles.Bold, new Color(0.63f, 0.72f, 0.8f));
        CreateSetupButton(panel, "Bomb Minus", "−", new Vector2(-40f, -38f), new Vector2(54f, 44f), () => ChangeMineCount(-1));
        setupMineLabel = CreateSetupText(panel, "Bomb Value", "", 22, new Vector2(40f, -38f), new Vector2(100f, 44f), FontStyles.Bold, Color.white);
        CreateSetupButton(panel, "Bomb Plus", "+", new Vector2(120f, -38f), new Vector2(54f, 44f), () => ChangeMineCount(1));
        setupEnvironmentLabel = CreateSetupText(panel, "Environment", "", 13, new Vector2(0f, -84f), new Vector2(400f, 26f), FontStyles.Bold, new Color(0.35f, 0.86f, 0.88f));
        CreateSetupButton(panel, "Start Game", "DEPLOY  →", new Vector2(0f, -137f), new Vector2(320f, 56f), StartGame, new Color(0.08f, 0.62f, 0.68f, 1f));
        RefreshSetupLabels();
        UpdateBackgroundForBoardSize();
    }

    private void BuildRuntimeUI()
    {
        var canvasObject = new GameObject("Touch Controls Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        runtimeCanvas = canvasObject.GetComponent<Canvas>();
        runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        runtimeCanvas.overrideSorting = true;
        runtimeCanvas.sortingOrder = 60;
        canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        openModeButton = CreateHudButton(canvasObject.transform, "Open Mode", "OPEN", SetOpenMode, new Color(0.08f, 0.62f, 0.68f, 1f));
        flagModeButton = CreateHudButton(canvasObject.transform, "Flag Mode", "FLAG", SetFlagMode, new Color(0.16f, 0.22f, 0.3f, 1f));
        hintButton = CreateHudButton(canvasObject.transform, "Hint", "HINT", ShowHint, new Color(0.45f, 0.62f, 0.24f, 1f));
        runtimeResetButton = CreateHudButton(canvasObject.transform, "Touch Restart", "RESTART", ReturnToSetup, new Color(0.1f, 0.15f, 0.22f, 1f));
        runtimeCanvas.gameObject.SetActive(false);
        RefreshRuntimeButtonLayout();
        UpdateRuntimeButtonState();
    }

    private Button CreateHudButton(Transform parent, string objectName, string label, UnityAction action, Color color)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(130f, 34f);
        var image = go.GetComponent<Image>();
        image.color = color;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.onClick.AddListener(action);
        button.colors = new ColorBlock
        {
            normalColor = color,
            highlightedColor = Color.Lerp(color, Color.white, 0.18f),
            pressedColor = Color.Lerp(color, Color.black, 0.18f),
            selectedColor = color,
            disabledColor = new Color(color.r, color.g, color.b, 0.45f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
        CreateSetupText(go.transform, "Label", label, 13, Vector2.zero, new Vector2(130f, 34f), FontStyles.Bold, Color.white);
        return button;
    }

    private void RefreshRuntimeButtonLayout()
    {
        if (runtimeCanvas == null || openModeButton == null ||
            (lastRuntimeScreenWidth == Screen.width && lastRuntimeScreenHeight == Screen.height)) return;
        lastRuntimeScreenWidth = Screen.width;
        lastRuntimeScreenHeight = Screen.height;

        // Keep touch controls at the bottom edge, away from the top HUD and board.
        // Width adapts to narrow portrait screens while the row stays centered.
        float buttonWidth = Mathf.Min(112f, (Screen.width - 40f - 24f) / 4f);
        float buttonHeight = Mathf.Clamp(buttonWidth * 0.46f, 28f, 36f);
        float spacing = 8f;
        float step = buttonWidth + spacing;
        float rowY = 16f;
        Vector2 size = new Vector2(buttonWidth, buttonHeight);
        SetHudButtonLayout(openModeButton, new Vector2(-step * 1.5f, rowY), size);
        SetHudButtonLayout(flagModeButton, new Vector2(-step * 0.5f, rowY), size);
        SetHudButtonLayout(hintButton, new Vector2(step * 0.5f, rowY), size);
        SetHudButtonLayout(runtimeResetButton, new Vector2(step * 1.5f, rowY), size);
    }

    private static void SetHudButtonLayout(Button button, Vector2 position, Vector2 size)
    {
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var label = button.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.rectTransform.sizeDelta = size;
            label.fontSize = Mathf.Clamp(size.y * 0.43f, 8f, 13f);
        }
    }

    private void SetOpenMode()
    {
        flagMode = false;
        UpdateRuntimeButtonState();
    }

    private void SetFlagMode()
    {
        flagMode = true;
        UpdateRuntimeButtonState();
    }

    private void ShowHint()
    {
        if (!gameStarted || gameEnded) return;

        var safeChoices = cells.Where(c => c.belongsToBoard && !c.isMine && !c.isRevealed && !c.isFlagged).ToList();
        if (safeChoices.Count == 0)
        {
            statusMessage = "No safe unrevealed tiles remain.";
            return;
        }

        Cell hint = safeChoices[Random.Range(0, safeChoices.Count)];
        flagMode = false;
        ClearAllHints();
        hint.isHinted = true;
        if (hint.coveredRenderer != null) hint.coveredRenderer.color = new Color(0.68f, 1f, 0.42f, 1f);
        StartCoroutine(PulseHint(hint));
        SpawnBurst(hint.root.transform.position, new Color(0.66f, 1f, 0.38f), 20);
        statusMessage = "Safe tile highlighted. Tap it to reveal.";
        UpdateRuntimeButtonState();
    }

    private IEnumerator PulseHint(Cell cell)
    {
        float duration = 2.4f;
        float elapsedTime = 0f;
        Vector3 baseScale = cell.baseScale;
        while (elapsedTime < duration && cell.isHinted && !cell.isRevealed)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float pulse = 1f + Mathf.Sin(elapsedTime * 10f) * 0.08f;
            cell.root.transform.localScale = baseScale * pulse;
            yield return null;
        }
        if (cell.isHinted && !cell.isRevealed) cell.root.transform.localScale = cell.baseScale;
    }

    private void ClearAllHints()
    {
        foreach (var cell in cells)
            if (cell.isHinted) ClearHint(cell);
    }

    private void ClearHint(Cell cell)
    {
        if (!cell.isHinted) return;
        cell.isHinted = false;
        if (cell.coveredRenderer != null) cell.coveredRenderer.color = Color.white;
        cell.root.transform.localScale = cell.baseScale * (cell.isPressed ? 0.93f : 1f);
    }

    private void UpdateRuntimeButtonState()
    {
        SetHudButtonColor(openModeButton, flagMode ? new Color(0.12f, 0.18f, 0.25f, 1f) : new Color(0.08f, 0.66f, 0.72f, 1f));
        SetHudButtonColor(flagModeButton, flagMode ? new Color(1f, 0.34f, 0.46f, 1f) : new Color(0.12f, 0.18f, 0.25f, 1f));
    }

    private static void SetHudButtonColor(Button button, Color color)
    {
        if (button == null) return;
        var image = button.GetComponent<Image>();
        image.color = color;
        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
        button.colors = colors;
    }

    private RectTransform CreatePanel(Transform parent, string objectName, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        return rect;
    }

    private TMP_Text CreateSetupText(Transform parent, string objectName, string value, float fontSize, Vector2 position, Vector2 size, FontStyles fontStyle, Color color)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = go.GetComponent<TMP_Text>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private void CreateSetupButton(Transform parent, string objectName, string label, Vector2 position, Vector2 size, UnityAction action, Color? colorOverride = null)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.color = colorOverride ?? new Color(0.2f, 0.3f, 0.4f, 1f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = image.color,
            highlightedColor = Color.Lerp(image.color, Color.white, 0.18f),
            pressedColor = Color.Lerp(image.color, Color.black, 0.18f),
            selectedColor = image.color,
            disabledColor = new Color(image.color.r, image.color.g, image.color.b, 0.45f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
        button.onClick.AddListener(action);
        CreateSetupText(go.transform, "Label", label, 20, Vector2.zero, size, FontStyles.Bold, Color.white);
    }

    private void ChangeGridSize(int amount)
    {
        gridSize = Mathf.Clamp(gridSize + amount, 5, 25);
        ClampMineCount();
        UpdateBackgroundForBoardSize();
        RefreshSetupLabels();
    }

    private void ChangeMineCount(int amount)
    {
        mineCount = Mathf.Clamp(mineCount + amount, MinMinesForGrid(), MaxMinesForGrid());
        RefreshSetupLabels();
    }

    private void RefreshSetupLabels()
    {
        if (setupGridLabel != null) setupGridLabel.text = gridSize + " × " + gridSize;
        if (setupMineLabel != null) setupMineLabel.text = mineCount.ToString("000");
    }

    private void StartGame()
    {
        PrepareBoardForSize();
        gameStarted = true;
        ResetGame();
    }

    private void PrepareBoardForSize()
    {
        if (blockTemplate == null || boardParent == null) return;
        UpdateBackgroundForBoardSize();

        int requiredCount = gridSize * gridSize;
        while (cells.Count < requiredCount)
        {
            var root = Instantiate(blockTemplate, boardParent);
            root.name = "Block (Generated)";
            root.SetActive(false);
            var cell = new Cell
            {
                root = root,
                coveredFace = root.transform.Find("BlockFront")?.gameObject,
                flagFace = root.transform.Find("BlockFlag")?.gameObject,
                coveredRenderer = root.transform.Find("BlockFront")?.GetComponentInChildren<SpriteRenderer>(),
                baseScale = root.transform.localScale
            };
            cells.Add(cell);
            cellByRoot.Add(root, cell);
        }

        columns = rows = gridSize;
        boardGrid = new Cell[gridSize, gridSize];
        float centerOffset = (gridSize - 1) * 0.5f;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            cell.belongsToBoard = i < requiredCount;
            if (cell.belongsToBoard)
            {
                cell.column = i % gridSize;
                cell.row = i / gridSize;
                boardGrid[cell.column, cell.row] = cell;
                cell.root.transform.localPosition = new Vector3(
                    cell.column - centerOffset,
                    cell.row - centerOffset,
                    0f);
            }
            else
            {
                cell.column = -1;
                cell.row = -1;
            }
            cell.root.SetActive(cell.belongsToBoard);
        }

        if (boardBackdrop != null)
        {
            boardBackdrop.position = boardParent.position;
            var backdropRenderer = boardBackdrop.GetComponent<SpriteRenderer>();
            float baseWorldWidth = backdropRenderer != null && backdropRenderer.sprite != null
                ? backdropRenderer.sprite.bounds.size.x * Mathf.Abs(backdropBaseScale.x)
                : 6.4f;
            float cellWorldWidth = Mathf.Abs(boardParent.lossyScale.x);
            float desiredWorldWidth = gridSize * cellWorldWidth + backdropPaddingWorld;
            boardBackdrop.localScale = backdropBaseScale * (desiredWorldWidth / baseWorldWidth);
        }

        if (sceneCamera != null && sceneCamera.orthographic)
        {
            sceneCamera.transform.position = new Vector3(boardParent.position.x, boardParent.position.y, sceneCamera.transform.position.z);
            // Reserve extra vertical margin for the bottom touch-control row.
            float requiredSize = gridSize * Mathf.Abs(boardParent.lossyScale.y) * 0.5f + 1.7f;
            var backdropRenderer = boardBackdrop != null ? boardBackdrop.GetComponent<SpriteRenderer>() : null;
            if (backdropRenderer != null) requiredSize = Mathf.Max(requiredSize, backdropRenderer.bounds.size.y * 0.5f + 0.25f);
            sceneCamera.orthographicSize = Mathf.Max(5f, requiredSize);
        }
    }

    private void UpdateBackgroundForBoardSize()
    {
        string environmentName = "STANDARD";
        Sprite background = defaultBackdropSprite;
        Sprite atmosphere = grassAtmosphere;
        Color exteriorColor = new Color(0.035f, 0.055f, 0.08f, 1f);
        Color labelColor = new Color(0.65f, 0.75f, 0.82f);
        if (gridSize >= 9 && gridSize <= 14)
        {
            environmentName = "GRASSLAND";
            background = grassBackground != null ? grassBackground : defaultBackdropSprite;
            atmosphere = grassAtmosphere;
            exteriorColor = new Color(0.035f, 0.12f, 0.085f, 1f);
            labelColor = new Color(0.43f, 0.88f, 0.57f);
        }
        else if (gridSize >= 15 && gridSize <= 20)
        {
            environmentName = "OCEAN";
            background = seaBackground != null ? seaBackground : defaultBackdropSprite;
            atmosphere = seaAtmosphere;
            exteriorColor = new Color(0.012f, 0.055f, 0.11f, 1f);
            labelColor = new Color(0.31f, 0.78f, 0.98f);
        }
        else if (gridSize >= 21 && gridSize <= 25)
        {
            environmentName = "VOLCANIC ZONE";
            background = volcanoBackground != null ? volcanoBackground : defaultBackdropSprite;
            atmosphere = volcanoAtmosphere;
            exteriorColor = new Color(0.105f, 0.035f, 0.048f, 1f);
            labelColor = new Color(1f, 0.49f, 0.32f);
        }

        var renderer = boardBackdrop != null ? boardBackdrop.GetComponent<SpriteRenderer>() : null;
        if (renderer != null && background != null) renderer.sprite = background;
        if (atmosphereRenderer != null) atmosphereRenderer.sprite = atmosphere;
        if (sceneCamera != null) sceneCamera.backgroundColor = exteriorColor;
        FitThemeAtmosphere();
        if (setupEnvironmentLabel != null)
        {
            setupEnvironmentLabel.text = "ENVIRONMENT  /  " + environmentName;
            setupEnvironmentLabel.color = labelColor;
        }
    }

    private void CreateThemeAtmosphere()
    {
        if (sceneCamera == null || atmosphereRenderer != null) return;
        var atmosphereObject = new GameObject("Theme Atmosphere");
        atmosphereObject.transform.position = new Vector3(sceneCamera.transform.position.x, sceneCamera.transform.position.y, 0f);
        atmosphereRenderer = atmosphereObject.AddComponent<SpriteRenderer>();
        atmosphereRenderer.sortingOrder = -100;
    }

    private void FitThemeAtmosphere()
    {
        if (sceneCamera == null || atmosphereRenderer == null || atmosphereRenderer.sprite == null || !sceneCamera.orthographic) return;
        float height = sceneCamera.orthographicSize * 2f;
        float width = height * sceneCamera.aspect;
        Vector3 desiredPosition = new Vector3(sceneCamera.transform.position.x, sceneCamera.transform.position.y, 0f);
        Vector3 desiredScale = new Vector3(
            width / atmosphereRenderer.sprite.bounds.size.x,
            height / atmosphereRenderer.sprite.bounds.size.y,
            1f);
        Transform atmosphereTransform = atmosphereRenderer.transform;
        if ((atmosphereTransform.position - desiredPosition).sqrMagnitude > 0.0001f) atmosphereTransform.position = desiredPosition;
        if ((atmosphereTransform.localScale - desiredScale).sqrMagnitude > 0.0001f) atmosphereTransform.localScale = desiredScale;
    }

    private static Sprite LoadBackground(string assetName)
    {
        var sprites = Resources.LoadAll<Sprite>("Backgrounds/" + assetName);
        return sprites.Length > 0 ? sprites[0] : null;
    }

    private int MaxMinesForGrid()
    {
        int totalCells = gridSize * gridSize;
        // Keep the first-click 3x3 safe area empty, and cap the density at 40%.
        return Mathf.Max(MinMinesForGrid(), Mathf.Min(Mathf.FloorToInt(totalCells * 0.4f), totalCells - 9));
    }

    private int MinMinesForGrid()
    {
        return Mathf.CeilToInt(gridSize * gridSize * 0.1f);
    }

    private void ClampMineCount()
    {
        mineCount = Mathf.Clamp(mineCount, MinMinesForGrid(), MaxMinesForGrid());
    }

    private void DrawNumbers()
    {
        foreach (var cell in cells)
        {
            int adjacentMines = CountAdjacentMines(cell);
            if (!cell.isRevealed || (adjacentMines == 0 && !cell.isMine)) continue;
            Vector3 point = Camera.main != null ? Camera.main.WorldToScreenPoint(cell.root.transform.position) : Vector3.zero;
            if (point.z < 0) continue;
            float screenY = Screen.height - point.y;
            float size = Mathf.Clamp(Screen.height / Mathf.Max(rows + 5f, 10f), 18f, 46f);
            cellNumberStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(size * 0.54f), 10, 24);
            GUI.color = cell.isMine ? new Color(0.9f, 0.12f, 0.12f) : NumberColor(adjacentMines);
            string label = cell.isMine ? "X" : adjacentMines.ToString();
            GUI.Label(new Rect(point.x - size / 2f, screenY - size / 2f, size, size), label, cellNumberStyle);
        }
        GUI.color = Color.white;
    }

    private static Color NumberColor(int number)
    {
        switch (number)
        {
            case 1: return new Color(0.28f, 0.70f, 1f);
            case 2: return new Color(0.28f, 0.88f, 0.65f);
            case 3: return new Color(1f, 0.40f, 0.49f);
            case 4: return new Color(0.67f, 0.57f, 1f);
            case 5: return new Color(1f, 0.67f, 0.34f);
            case 6: return new Color(0.30f, 0.86f, 0.93f);
            default: return new Color(0.79f, 0.87f, 0.93f);
        }
    }

    private void EnsureStyles()
    {
        if (headerStyle != null) return;
        panelTexture = MakeTexture(new Color(0.025f, 0.045f, 0.075f, 0.94f));
        buttonTexture = MakeTexture(new Color(0.08f, 0.14f, 0.2f, 0.96f));
        accentTexture = MakeTexture(new Color(0.14f, 0.82f, 0.87f, 1f));
        hudBoxStyle = new GUIStyle(GUI.skin.box) { normal = { background = panelTexture }, border = new RectOffset(10, 10, 10, 10), padding = new RectOffset(12, 12, 8, 8) };
        hudButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.84f, 0.94f, 0.98f), background = buttonTexture }, hover = { textColor = Color.white, background = accentTexture }, active = { textColor = Color.white, background = accentTexture } };
        headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.35f, 0.9f, 0.95f) } };
        cellNumberStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, wordWrap = true, normal = { textColor = new Color(0.58f, 0.7f, 0.78f) } };
        overlayStyle = new GUIStyle(GUI.skin.box) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = panelTexture } };
    }

    private Texture2D MakeTexture(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private float overlayStartedAt;

    private IEnumerator PopTransform(Transform target)
    {
        if (target == null) yield break;
        Vector3 end = target.localScale;
        target.localScale = end * 0.78f;
        const float duration = 0.12f;
        float elapsedTime = 0f;
        while (elapsedTime < duration && target != null)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsedTime / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            target.localScale = Vector3.LerpUnclamped(end * 0.78f, end, eased);
            yield return null;
        }
        if (target != null) target.localScale = end;
    }

    private void SpawnBurst(Vector3 position, Color color, short particleCount = 24)
    {
        var effectObject = new GameObject("Board Burst");
        effectObject.transform.position = position;
        effectObject.SetActive(false);
        var particles = effectObject.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.duration = 0.35f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.52f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.085f);
        main.startColor = color;
        main.maxParticles = 48;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, particleCount) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;
        effectObject.SetActive(true);
        particles.Play();
        Destroy(effectObject, 1.5f);
    }

    private void SpawnWinBursts()
    {
        foreach (var cell in cells.Where(c => c.belongsToBoard && c.isRevealed).Take(12))
            SpawnBurst(cell.root.transform.position, new Color(0.22f, 0.9f, 0.72f), 14);
    }

    private void BindWeaponButton()
    {
        weaponButton = FindAnyObjectByType<Button>();
        if (weaponButton == null) return;
        weaponButton.onClick.RemoveListener(ToggleFlagMode);
        weaponButton.onClick.AddListener(ToggleFlagMode);
        var image = weaponButton.GetComponent<Image>();
        if (image != null) image.color = new Color(0.16f, 0.24f, 0.32f, 0.95f);
        var label = weaponButton.GetComponentInChildren<TMP_Text>();
        if (label == null)
        {
            var labelObject = new GameObject("Weapon Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(weaponButton.transform, false);
            var rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            label = labelObject.GetComponent<TMP_Text>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 18;
            label.color = Color.white;
            label.raycastTarget = false;
        }
        label.text = "FLAG";
        weaponButton.gameObject.SetActive(false);
    }

    private void ToggleFlagMode()
    {
        flagMode = !flagMode;
        UpdateRuntimeButtonState();
    }
}
