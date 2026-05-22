using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public enum PlatformKind
{
    Green,
    Red,
    Gold,
    Life,
    Time
}

public enum BallLook
{
    Yellow,
    White,
    WhitePurpleGlow
}

public sealed class ArcadeBounceGame : MonoBehaviour
{
    private const float BallStartY = 0.9f;
    private const float ManualJumpCooldown = 0.18f;

    [System.Serializable]
    private sealed class LevelConfig
    {
        public int targetScore;
        public float timeLimit;
        public float platformInterval;
        public float redChance;
        public float goldChance;
        public float lifeChance;
        public float timeChance;
        public float platformSpeed;
        public float ballStartBounce;
        public float ballMaxBounce;
        public Color backgroundA;
        public Color backgroundB;
        public float musicPitch;
    }

    private readonly LevelConfig[] levels =
    {
        new LevelConfig
        {
            targetScore = 10,
            timeLimit = 90f,
            platformInterval = 1.05f,
            redChance = 0.28f,
            goldChance = 0f,
            lifeChance = 0.11f,
            timeChance = 0.07f,
            platformSpeed = 1.55f,
            ballStartBounce = 9.5f,
            ballMaxBounce = 12f,
            backgroundA = new Color(0.18f, 0.62f, 0.95f),
            backgroundB = new Color(0.52f, 0.86f, 0.58f),
            musicPitch = 0.95f
        },
        new LevelConfig
        {
            targetScore = 20,
            timeLimit = 60f,
            platformInterval = 0.72f,
            redChance = 0.46f,
            goldChance = 0.09f,
            lifeChance = 0.10f,
            timeChance = 0.09f,
            platformSpeed = 2.75f,
            ballStartBounce = 11.5f,
            ballMaxBounce = 17f,
            backgroundA = new Color(0.78f, 0.62f, 1f),
            backgroundB = new Color(0.78f, 0.62f, 1f),
            musicPitch = 1.25f
        },
        new LevelConfig
        {
            targetScore = 32,
            timeLimit = 70f,
            platformInterval = 0.58f,
            redChance = 0.38f,
            goldChance = 0.14f,
            lifeChance = 0.08f,
            timeChance = 0.14f,
            platformSpeed = 3.45f,
            ballStartBounce = 12.5f,
            ballMaxBounce = 19f,
            backgroundA = new Color(0.02f, 0.06f, 0.18f),
            backgroundB = new Color(0.05f, 0.75f, 0.88f),
            musicPitch = 1.45f
        }
    };

    private Camera mainCamera;
    private Rigidbody2D ballBody;
    private SpriteRenderer ballRenderer;
    private SpriteRenderer ballGlowRenderer;
    private ParticleSystem jumpParticles;
    private ParticleSystem scoreParticles;
    private AudioSource musicSource;
    private AudioSource sfxSource;
    private CanvasGroup fadeGroup;
    private Canvas menuCanvas;
    private Canvas hudCanvas;
    private Canvas resultCanvas;
    private Text scoreText;
    private Text timerText;
    private Text levelText;
    private Text resultTitle;
    private Text resultStats;
    private Text resultStars;
    private Button continueButton;
    private Button restartButton;
    private Button menuButton;
    private Transform platformsRoot;
    private Transform effectsRoot;
    private Coroutine spawnRoutine;
    private Coroutine shakeRoutine;
    private int currentLevel;
    private int score;
    private int lives;
    private float remainingTime;
    private bool playing;
    private bool gameEnded;
    private float worldHalfWidth;
    private float worldHalfHeight;
    private Vector3 cameraHome;
    private float lastManualJumpTime = -10f;
    private BallLook selectedBallLook = BallLook.Yellow;
    private readonly Text[] heartTexts = new Text[3];

    private void Awake()
    {
        Application.targetFrameRate = 90;
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = new GameObject("Main Camera").AddComponent<Camera>();
            mainCamera.tag = "MainCamera";
            mainCamera.gameObject.AddComponent<AudioListener>();
        }

        mainCamera.orthographic = true;
        mainCamera.orthographicSize = 6f;
        mainCamera.transform.position = new Vector3(0f, 0f, -10f);
        cameraHome = mainCamera.transform.position;
        worldHalfHeight = mainCamera.orthographicSize;
        worldHalfWidth = worldHalfHeight * mainCamera.aspect;

        platformsRoot = new GameObject("Runtime Platforms").transform;
        effectsRoot = new GameObject("Runtime Effects").transform;
        CreateAudio();
        CreateBall();
        CreateWorldBounds();
        CreateUi();
        ShowMenu();
    }

    private void Update()
    {
        if (!playing)
        {
            return;
        }

        LevelConfig cfg = levels[currentLevel];
        remainingTime -= Time.deltaTime;
        if (remainingTime <= 0f)
        {
            EndLevel(false);
            return;
        }

        float t = Mathf.PingPong(Time.time * (currentLevel == 0 ? 0.10f : currentLevel == 1 ? 0.32f : 0.55f), 1f);
        mainCamera.backgroundColor = Color.Lerp(cfg.backgroundA, cfg.backgroundB, Smooth(t));

        float move = 0f;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A))
        {
            move -= 1f;
        }
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D))
        {
            move += 1f;
        }
        move = Mathf.Clamp(move, -1f, 1f);

        float xSpeed = currentLevel == 0 ? 7.2f : currentLevel == 1 ? 8.8f : 9.8f;
        ballBody.linearVelocity = new Vector2(move * xSpeed, ballBody.linearVelocity.y);

        if (Input.GetKeyDown(KeyCode.Space) && Time.time - lastManualJumpTime >= ManualJumpCooldown)
        {
            lastManualJumpTime = Time.time;
            BounceBall(0.92f);
            jumpParticles.transform.position = ballBody.position;
            jumpParticles.Play();
            PlayJumpSound();
        }

        if (currentLevel > 0)
        {
            float ramp = Mathf.InverseLerp(cfg.timeLimit, 0f, remainingTime);
            ballBody.gravityScale = currentLevel == 1 ? Mathf.Lerp(2.5f, 3.25f, ramp) : Mathf.Lerp(2.65f, 3.65f, ramp);
        }

        if (ballBody.position.y < -worldHalfHeight - 1.5f)
        {
            LoseLife();
            ResetBall();
        }

        UpdateHud();
    }

    public void OnPlatformHit(BouncePlatform platform)
    {
        if (!playing || platform == null)
        {
            return;
        }

        if (platform.Kind == PlatformKind.Red)
        {
            LoseLife();
            PlayRedSound();
        }
        else
        {
            int points = platform.Kind == PlatformKind.Gold ? 3 : 1;
            if (platform.Kind == PlatformKind.Life)
            {
                lives = Mathf.Min(3, lives + 1);
                points = 2;
                PlayLifeSound();
            }
            else if (platform.Kind == PlatformKind.Time)
            {
                remainingTime = Mathf.Min(levels[currentLevel].timeLimit, remainingTime + 6f);
                points = 1;
                PlayTimeSound();
            }
            else
            {
                PlayGreenSound(platform.Kind == PlatformKind.Gold);
            }

            score += points;
            scoreParticles.transform.position = platform.transform.position;
            scoreParticles.Play();
            if (score >= levels[currentLevel].targetScore)
            {
                EndLevel(true);
            }
        }

        BounceBall(platform.Kind == PlatformKind.Red ? 0.78f : 1f);
        jumpParticles.transform.position = ballBody.position;
        jumpParticles.Play();
    }

    private void ShowMenu()
    {
        playing = false;
        gameEnded = false;
        ClearPlatforms();
        ResetBall();
        ballBody.simulated = false;
        hudCanvas.gameObject.SetActive(false);
        resultCanvas.gameObject.SetActive(false);
        menuCanvas.gameObject.SetActive(true);
        musicSource.pitch = 0.82f;
        musicSource.Stop();
        StartCoroutine(FadeTo(0f));
    }

    private void StartLevel(int levelIndex)
    {
        currentLevel = levelIndex;
        LevelConfig cfg = levels[currentLevel];
        score = 0;
        lives = 3;
        remainingTime = cfg.timeLimit;
        gameEnded = false;
        playing = true;
        musicSource.pitch = cfg.musicPitch;
        musicSource.Stop();
        ClearPlatforms();
        ResetBall();
        ballBody.simulated = true;
        ballBody.gravityScale = currentLevel == 0 ? 2.35f : currentLevel == 1 ? 2.5f : 2.65f;
        BounceBall(1f);
        menuCanvas.gameObject.SetActive(false);
        resultCanvas.gameObject.SetActive(false);
        hudCanvas.gameObject.SetActive(true);
        UpdateHud();

        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
        }

        spawnRoutine = StartCoroutine(SpawnPlatforms());
        StartCoroutine(FadeTo(0f));
    }

    private void EndLevel(bool won)
    {
        if (gameEnded)
        {
            return;
        }

        gameEnded = true;
        playing = false;
        ballBody.simulated = false;
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
        }

        PlayTone(won ? 740f : 130f, won ? 0.5f : 0.7f, won ? 0.6f : 0.5f);
        StartCoroutine(ShowResultAfterFade(won));
    }

    private IEnumerator ShowResultAfterFade(bool won)
    {
        yield return FadeTo(1f);
        hudCanvas.gameObject.SetActive(false);
        resultCanvas.gameObject.SetActive(true);

        bool finalWin = won && currentLevel == levels.Length - 1;
        resultTitle.text = won ? (finalWin ? "Congratulations!" : "Level Complete!") : "Game Over";
        resultTitle.color = won ? new Color(1f, 0.86f, 0.22f) : new Color(1f, 0.28f, 0.34f);
        int stars = CalculateStars(won);
        resultStars.text = Stars(stars);
        resultStats.text =
            "Score: " + score + "/" + levels[currentLevel].targetScore +
            "\nTime Left: " + Mathf.CeilToInt(Mathf.Max(0f, remainingTime)) + "s" +
            "\nLives: " + lives + "/3" +
            "\nRating: " + stars + " Star" + (stars == 1 ? "" : "s");

        continueButton.gameObject.SetActive(won && currentLevel < levels.Length - 1);
        if (won && currentLevel < levels.Length - 1)
        {
            continueButton.GetComponentInChildren<Text>().text = "Continue Level " + (currentLevel + 2);
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(() => StartLevel(currentLevel + 1));
        }
        restartButton.gameObject.SetActive(!won || finalWin);
        menuButton.gameObject.SetActive(true);
        yield return FadeTo(0f);
    }

    private IEnumerator SpawnPlatforms()
    {
        while (playing)
        {
            SpawnPlatform();
            float interval = levels[currentLevel].platformInterval;
            if (currentLevel > 0)
            {
                float endSpeed = currentLevel == 1 ? 0.72f : 0.55f;
                interval *= Mathf.Lerp(1f, endSpeed, Mathf.InverseLerp(levels[currentLevel].timeLimit, 0f, remainingTime));
            }
            yield return new WaitForSeconds(interval);
        }
    }

    private void SpawnPlatform()
    {
        LevelConfig cfg = levels[currentLevel];
        float roll = Random.value;
        PlatformKind kind = PickPlatformKind(cfg, roll);
        GameObject obj = new GameObject(kind + " Platform");
        obj.transform.SetParent(platformsRoot);
        obj.transform.position = new Vector3(Random.Range(-worldHalfWidth + 1.2f, worldHalfWidth - 1.2f), worldHalfHeight + 0.8f, 0f);
        obj.transform.localScale = new Vector3(kind == PlatformKind.Gold || kind == PlatformKind.Time ? 1.8f : Random.Range(1.7f, 2.6f), 0.28f, 1f);

        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = MakeSprite();
        sr.color = PlatformColor(kind);
        sr.sortingOrder = 2;

        AddPlatformMarker(obj.transform, kind);

        BoxCollider2D collider = obj.AddComponent<BoxCollider2D>();
        collider.sharedMaterial = null;

        Rigidbody2D body = obj.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;

        BouncePlatform platform = obj.AddComponent<BouncePlatform>();
        platform.Setup(this, kind, cfg.platformSpeed * (currentLevel == 0 ? 1f : Random.Range(0.9f, currentLevel == 1 ? 1.35f : 1.65f)), currentLevel);
    }

    private PlatformKind PickPlatformKind(LevelConfig cfg, float roll)
    {
        if (roll < cfg.goldChance)
        {
            return PlatformKind.Gold;
        }
        if (roll < cfg.goldChance + cfg.lifeChance)
        {
            return PlatformKind.Life;
        }
        if (roll < cfg.goldChance + cfg.lifeChance + cfg.timeChance)
        {
            return PlatformKind.Time;
        }
        if (roll < cfg.goldChance + cfg.lifeChance + cfg.timeChance + cfg.redChance)
        {
            return PlatformKind.Red;
        }
        return PlatformKind.Green;
    }

    private static Color PlatformColor(PlatformKind kind)
    {
        if (kind == PlatformKind.Red)
        {
            return new Color(1f, 0.16f, 0.22f);
        }
        if (kind == PlatformKind.Gold)
        {
            return new Color(1f, 0.75f, 0.06f);
        }
        if (kind == PlatformKind.Time)
        {
            return new Color(0.15f, 0.88f, 1f);
        }
        return new Color(0.1f, 0.95f, 0.38f);
    }

    private void AddPlatformMarker(Transform parent, PlatformKind kind)
    {
        if (kind != PlatformKind.Life && kind != PlatformKind.Time)
        {
            return;
        }

        GameObject marker = new GameObject(kind + " Marker");
        marker.transform.SetParent(parent);
        marker.transform.localPosition = new Vector3(0f, 0.38f, 0f);
        marker.transform.localScale = kind == PlatformKind.Life ? Vector3.one * 0.45f : Vector3.one * 0.38f;
        SpriteRenderer markerRenderer = marker.AddComponent<SpriteRenderer>();
        markerRenderer.sprite = kind == PlatformKind.Life ? MakeHeartSprite(64) : MakeClockSprite(64);
        markerRenderer.color = kind == PlatformKind.Life ? new Color(1f, 0.05f, 0.20f) : Color.white;
        markerRenderer.sortingOrder = 4;
    }

    private void BounceBall(float multiplier)
    {
        LevelConfig cfg = levels[currentLevel];
        float ramp = Mathf.InverseLerp(cfg.timeLimit, 0f, remainingTime);
        float bounce = Mathf.Lerp(cfg.ballStartBounce, cfg.ballMaxBounce, currentLevel == 0 ? ramp * 0.35f : ramp);
        ballBody.linearVelocity = new Vector2(ballBody.linearVelocity.x, bounce * multiplier);
    }

    private void LoseLife()
    {
        if (!playing)
        {
            return;
        }

        lives = Mathf.Max(0, lives - 1);
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
        }
        shakeRoutine = StartCoroutine(CameraShake(0.16f, 0.12f));
        UpdateHud();
        if (lives <= 0)
        {
            EndLevel(false);
        }
    }

    private void ResetBall()
    {
        if (ballBody == null)
        {
            return;
        }

        ballBody.position = new Vector2(0f, BallStartY);
        ballBody.linearVelocity = Vector2.up * 3.5f;
        lastManualJumpTime = -10f;
        ApplyBallLook();
    }

    private void UpdateHud()
    {
        scoreText.text = "Score " + score + "/" + levels[currentLevel].targetScore;
        timerText.text = "Time " + Mathf.CeilToInt(Mathf.Max(0f, remainingTime));
        levelText.text = "Level " + (currentLevel + 1);
        for (int i = 0; i < heartTexts.Length; i++)
        {
            heartTexts[i].text = i < lives ? "♥" : "♡";
            heartTexts[i].color = i < lives ? new Color(1f, 0.18f, 0.28f) : new Color(0.65f, 0.65f, 0.7f);
        }
    }

    private int CalculateStars(bool won)
    {
        float timeRatio = Mathf.Clamp01(remainingTime / levels[currentLevel].timeLimit);
        if (!won)
        {
            return Mathf.Clamp(Mathf.CeilToInt((float)score / levels[currentLevel].targetScore * 2f), 1, 2);
        }
        if (timeRatio >= 0.42f && lives >= 2)
        {
            return 3;
        }
        if (timeRatio >= 0.18f || lives >= 2)
        {
            return 2;
        }
        return 1;
    }

    private static string Stars(int count)
    {
        return count == 3 ? "★ ★ ★" : count == 2 ? "★ ★ ☆" : "★ ☆ ☆";
    }

    private void ClearPlatforms()
    {
        for (int i = platformsRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(platformsRoot.GetChild(i).gameObject);
        }
    }

    private IEnumerator CameraShake(float duration, float amount)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            mainCamera.transform.position = cameraHome + (Vector3)Random.insideUnitCircle * amount;
            yield return null;
        }
        mainCamera.transform.position = cameraHome;
    }

    private IEnumerator FadeTo(float target)
    {
        float start = fadeGroup.alpha;
        float elapsed = 0f;
        while (elapsed < 0.35f)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.Lerp(start, target, Smooth(elapsed / 0.35f));
            yield return null;
        }
        fadeGroup.alpha = target;
    }

    private void CreateBall()
    {
        GameObject ball = new GameObject("Glowing Player Ball");
        ball.transform.position = new Vector3(0f, BallStartY, 0f);
        ballRenderer = ball.AddComponent<SpriteRenderer>();
        ballRenderer.sprite = MakeCircleSprite(64, 0.48f);
        ballRenderer.color = new Color(1f, 0.93f, 0.22f);
        ballRenderer.sortingOrder = 5;
        ball.transform.localScale = Vector3.one * 0.95f;

        CircleCollider2D collider = ball.AddComponent<CircleCollider2D>();
        collider.radius = 0.5f;
        PhysicsMaterial2D mat = new PhysicsMaterial2D("Bouncy Ball") { bounciness = 0.35f, friction = 0f };
        collider.sharedMaterial = mat;

        ballBody = ball.AddComponent<Rigidbody2D>();
        ballBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        ballBody.freezeRotation = true;
        ballBody.gravityScale = 2.35f;
        ballBody.simulated = false;

        BallCollisionForwarder forwarder = ball.AddComponent<BallCollisionForwarder>();
        forwarder.Game = this;

        GameObject glow = new GameObject("Ball Soft Glow");
        glow.transform.SetParent(ball.transform);
        glow.transform.localPosition = Vector3.zero;
        glow.transform.localScale = Vector3.one * 2.2f;
        ballGlowRenderer = glow.AddComponent<SpriteRenderer>();
        ballGlowRenderer.sprite = MakeCircleSprite(64, 0.5f);
        ballGlowRenderer.color = new Color(1f, 0.86f, 0.18f, 0.22f);
        ballGlowRenderer.sortingOrder = 4;
        ApplyBallLook();

        jumpParticles = CreateParticles("Jump Particles", new Color(1f, 0.95f, 0.35f), 22);
        scoreParticles = CreateParticles("Score Sparkles", new Color(0.3f, 1f, 0.58f), 34);
    }

    private void CreateWorldBounds()
    {
        CreateWall("Left Wall", new Vector2(-worldHalfWidth - 0.25f, 0f), new Vector2(0.4f, worldHalfHeight * 2.3f));
        CreateWall("Right Wall", new Vector2(worldHalfWidth + 0.25f, 0f), new Vector2(0.4f, worldHalfHeight * 2.3f));
        CreateWall("Ceiling", new Vector2(0f, worldHalfHeight + 0.25f), new Vector2(worldHalfWidth * 2.3f, 0.4f));
    }

    private void CreateWall(string name, Vector2 position, Vector2 scale)
    {
        GameObject wall = new GameObject(name);
        wall.transform.position = position;
        BoxCollider2D box = wall.AddComponent<BoxCollider2D>();
        box.size = scale;
    }

    private void CreateAudio()
    {
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.volume = 0f;
        musicSource.clip = CreateLoopClip();
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.volume = 0.8f;
    }

    private AudioClip CreateLoopClip()
    {
        int sampleRate = 44100;
        int length = sampleRate * 4;
        float[] data = new float[length];
        float[] notes = { 261.63f, 329.63f, 392f, 523.25f, 392f, 329.63f, 440f, 392f };
        for (int i = 0; i < length; i++)
        {
            float time = (float)i / sampleRate;
            int beat = Mathf.FloorToInt(time * 2f) % notes.Length;
            float wave = Mathf.Sin(2f * Mathf.PI * notes[beat] * time) * 0.18f;
            wave += Mathf.Sin(2f * Mathf.PI * notes[beat] * 2f * time) * 0.04f;
            data[i] = wave * (0.7f + 0.3f * Mathf.Sin(time * Mathf.PI * 4f));
        }
        AudioClip clip = AudioClip.Create("Procedural Happy Loop", length, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void PlayTone(float frequency, float duration, float volume)
    {
        int sampleRate = 44100;
        int length = Mathf.CeilToInt(sampleRate * duration);
        float[] data = new float[length];
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = 1f - t / duration;
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * volume;
        }
        AudioClip clip = AudioClip.Create("Game Tone", length, 1, sampleRate, false);
        clip.SetData(data, 0);
        sfxSource.PlayOneShot(clip);
    }

    private void PlayGreenSound(bool gold)
    {
        PlayTone(gold ? 980f : 620f, gold ? 0.16f : 0.12f, 0.52f);
    }

    private void PlayRedSound()
    {
        PlayTone(120f, 0.20f, 0.50f);
    }

    private void PlayJumpSound()
    {
        PlayTone(720f, 0.08f, 0.35f);
        PlayTone(920f, 0.06f, 0.22f);
    }

    private void PlayLifeSound()
    {
        PlayTone(760f, 0.10f, 0.45f);
        PlayTone(1040f, 0.12f, 0.35f);
    }

    private void PlayTimeSound()
    {
        PlayTone(520f, 0.08f, 0.42f);
        PlayTone(880f, 0.12f, 0.34f);
    }

    private void CreateUi()
    {
        menuCanvas = CreateCanvas("Menu Canvas");
        hudCanvas = CreateCanvas("HUD Canvas");
        resultCanvas = CreateCanvas("Result Canvas");
        fadeGroup = CreateCanvas("Fade Canvas").gameObject.AddComponent<CanvasGroup>();
        Image fade = fadeGroup.gameObject.AddComponent<Image>();
        fade.color = Color.black;
        fade.raycastTarget = false;
        fadeGroup.alpha = 0f;

        BuildMenu();
        BuildHud();
        BuildResult();
    }

    private Canvas CreateCanvas(string name)
    {
        GameObject obj = new GameObject(name);
        Canvas canvas = obj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = name.StartsWith("Fade") ? 50 : 10;
        CanvasScaler scaler = obj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        obj.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private void BuildMenu()
    {
        Image bg = menuCanvas.gameObject.AddComponent<Image>();
        bg.color = new Color(0.01f, 0.04f, 0.16f);
        Text title = AddText(menuCanvas.transform, "rush", 78, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.74f), new Vector2(760f, 95f), Color.white);
        title.fontStyle = FontStyle.Bold;
        AddText(menuCanvas.transform, "Choose your ball color", 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.61f), new Vector2(820f, 55f), new Color(0.78f, 0.92f, 1f));
        AddButton(menuCanvas.transform, "Yellow Ball", new Vector2(0.34f, 0.50f), new Vector2(220f, 58f), new Color(0.22f, 0.74f, 1f), () => SelectBallLook(BallLook.Yellow));
        AddButton(menuCanvas.transform, "White Ball", new Vector2(0.50f, 0.50f), new Vector2(220f, 58f), new Color(0.22f, 0.74f, 1f), () => SelectBallLook(BallLook.White));
        AddButton(menuCanvas.transform, "Purple Glow", new Vector2(0.66f, 0.50f), new Vector2(220f, 58f), new Color(0.22f, 0.74f, 1f), () => SelectBallLook(BallLook.WhitePurpleGlow));
        AddButton(menuCanvas.transform, "Start", new Vector2(0.5f, 0.36f), new Vector2(260f, 70f), new Color(0.22f, 0.74f, 1f), () => StartLevel(0));
        AddButton(menuCanvas.transform, "Exit", new Vector2(0.5f, 0.24f), new Vector2(260f, 70f), new Color(0.22f, 0.74f, 1f), Application.Quit);
    }

    private void SelectBallLook(BallLook look)
    {
        selectedBallLook = look;
        ApplyBallLook();
        PlayTimeSound();
    }

    private void ApplyBallLook()
    {
        if (ballRenderer == null || ballGlowRenderer == null)
        {
            return;
        }

        if (selectedBallLook == BallLook.Yellow)
        {
            ballRenderer.color = new Color(1f, 0.93f, 0.22f);
            ballGlowRenderer.color = new Color(1f, 0.86f, 0.18f, 0.22f);
        }
        else if (selectedBallLook == BallLook.White)
        {
            ballRenderer.color = Color.white;
            ballGlowRenderer.color = new Color(1f, 1f, 1f, 0.22f);
        }
        else
        {
            ballRenderer.color = Color.white;
            ballGlowRenderer.color = new Color(0.64f, 0.16f, 1f, 0.45f);
        }
    }

    private void BuildHud()
    {
        scoreText = AddText(hudCanvas.transform, "Score 0", 34, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.93f), new Vector2(260f, 52f), Color.white);
        timerText = AddText(hudCanvas.transform, "Time 0", 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.93f), new Vector2(260f, 52f), Color.white);
        levelText = AddText(hudCanvas.transform, "Level 1", 34, TextAnchor.MiddleRight, new Vector2(0.82f, 0.93f), new Vector2(220f, 52f), Color.white);
        for (int i = 0; i < heartTexts.Length; i++)
        {
            heartTexts[i] = AddText(hudCanvas.transform, "♥", 42, TextAnchor.MiddleCenter, new Vector2(0.87f + i * 0.035f, 0.93f), new Vector2(45f, 52f), Color.red);
        }
    }

    private void BuildResult()
    {
        Image bg = resultCanvas.gameObject.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.16f);
        resultTitle = AddText(resultCanvas.transform, "Congratulations!", 68, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.73f), new Vector2(850f, 90f), Color.white);
        resultTitle.fontStyle = FontStyle.Bold;
        resultStars = AddText(resultCanvas.transform, "★ ★ ★", 58, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.58f), new Vector2(500f, 76f), new Color(1f, 0.8f, 0.1f));
        resultStats = AddText(resultCanvas.transform, "", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.43f), new Vector2(620f, 140f), Color.white);
        continueButton = AddButton(resultCanvas.transform, "Continue Level 2", new Vector2(0.5f, 0.25f), new Vector2(340f, 66f), new Color(0.1f, 0.72f, 1f), () => StartLevel(1));
        restartButton = AddButton(resultCanvas.transform, "Restart", new Vector2(0.39f, 0.20f), new Vector2(230f, 62f), new Color(0.1f, 0.92f, 0.46f), () => StartLevel(currentLevel));
        menuButton = AddButton(resultCanvas.transform, "Main Menu", new Vector2(0.61f, 0.20f), new Vector2(230f, 62f), new Color(1f, 0.65f, 0.15f), ShowMenu);
    }

    private Text AddText(Transform parent, string value, int size, TextAnchor anchor, Vector2 viewportPosition, Vector2 dimensions, Color color)
    {
        GameObject obj = new GameObject(value + " Text");
        obj.transform.SetParent(parent, false);
        Text text = obj.AddComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(14, size / 2);
        text.resizeTextMaxSize = size;
        text.alignment = anchor;
        text.color = color;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = viewportPosition;
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = Vector2.zero;
        return text;
    }

    private Button AddButton(Transform parent, string label, Vector2 viewportPosition, Vector2 dimensions, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = new GameObject(label + " Button");
        obj.transform.SetParent(parent, false);
        Image image = obj.AddComponent<Image>();
        image.color = color;
        Button button = obj.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = viewportPosition;
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = Vector2.zero;
        Text text = AddText(obj.transform, label, 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), dimensions, Color.white);
        text.fontStyle = FontStyle.Bold;
        return button;
    }

    private ParticleSystem CreateParticles(string name, Color color, int burstCount)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(effectsRoot);
        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = 0.45f;
        main.startSpeed = 4.5f;
        main.startSize = 0.12f;
        main.startColor = color;
        main.loop = false;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.2f;
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = 8;
        return ps;
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private static Sprite MakeSprite()
    {
        Texture2D texture = Texture2D.whiteTexture;
        return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
    }

    private static Sprite MakeCircleSprite(int size, float radius)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float pixelRadius = size * radius;
        float softEdge = Mathf.Max(1f, size * 0.04f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01((pixelRadius - distance) / softEdge);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite MakeHeartSprite(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (float)(size - 1) - 0.5f) * 2.4f;
                float ny = (y / (float)(size - 1) - 0.42f) * 2.4f;
                float a = nx * nx + ny * ny - 1f;
                bool inside = a * a * a - nx * nx * ny * ny * ny <= 0f;
                pixels[y * size + x] = inside ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite MakeClockSprite(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.42f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                float distance = Vector2.Distance(p, center);
                bool ring = distance < radius && distance > radius - 5f;
                bool hourHand = Mathf.Abs(x - center.x) < 2.5f && y >= center.y && y < center.y + radius * 0.48f;
                bool minuteHand = Mathf.Abs(y - center.y) < 2.5f && x >= center.x && x < center.x + radius * 0.55f;
                pixels[y * size + x] = ring || hourHand || minuteHand ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }
}

public sealed class BallCollisionForwarder : MonoBehaviour
{
    public ArcadeBounceGame Game;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        BouncePlatform platform = collision.collider.GetComponent<BouncePlatform>();
        if (platform != null)
        {
            platform.Hit();
        }
    }
}

public sealed class BouncePlatform : MonoBehaviour
{
    private ArcadeBounceGame game;
    private float speed;
    private int levelIndex;
    private bool hit;
    private float horizontalSpeed;
    private float driftSeed;
    private Rigidbody2D body;

    public PlatformKind Kind { get; private set; }

    public void Setup(ArcadeBounceGame owner, PlatformKind kind, float moveSpeed, int level)
    {
        game = owner;
        Kind = kind;
        speed = moveSpeed;
        levelIndex = level;
        body = GetComponent<Rigidbody2D>();
        horizontalSpeed = Random.Range(-1.4f, 1.4f) * (levelIndex == 2 ? 2.7f : levelIndex == 1 ? 1.8f : 0.8f);
        driftSeed = Random.Range(0f, 100f);
    }

    private void FixedUpdate()
    {
        Vector2 movement = Vector2.down * speed * Time.fixedDeltaTime;
        if (levelIndex > 0)
        {
            float wave = levelIndex == 2 ? Mathf.Sin(Time.time * 5.2f + driftSeed) * 2.2f : Mathf.Sin(Time.time * 3f + driftSeed) * 1.2f;
            movement += Vector2.right * ((horizontalSpeed + wave) * Time.fixedDeltaTime);
        }
        body.MovePosition(body.position + movement);

        if (levelIndex == 2)
        {
            float pulse = 1f + Mathf.Sin(Time.time * 6f + driftSeed) * 0.08f;
            transform.localScale = new Vector3(transform.localScale.x, 0.28f * pulse, transform.localScale.z);
        }
    }

    private void Update()
    {
        if (transform.position.y < -8f)
        {
            Destroy(gameObject);
        }
    }

    public void Hit()
    {
        if (hit)
        {
            return;
        }

        hit = true;
        game.OnPlatformHit(this);
        if (levelIndex > 0 || Kind == PlatformKind.Gold || Kind == PlatformKind.Life || Kind == PlatformKind.Time)
        {
            StartCoroutine(DisappearSoon());
        }
    }

    private IEnumerator DisappearSoon()
    {
        yield return new WaitForSeconds(levelIndex > 0 ? 2f : 0.15f);
        Destroy(gameObject);
    }
}
