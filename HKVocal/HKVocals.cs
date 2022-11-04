using System.Text.RegularExpressions;
using HKMirror.Hooks.OnHooks;
using HKMirror.Reflection.InstanceClasses;

namespace HKVocals;

public class HKVocals: Mod, IGlobalSettings<GlobalSettings>, ILocalSettings<SaveSettings>, ICustomMenuMod
{
    public static GlobalSettings _globalSettings { get; set; } = new GlobalSettings();
    public void OnLoadGlobal(GlobalSettings s) => _globalSettings = s;
    public GlobalSettings OnSaveGlobal() => _globalSettings;
    public static SaveSettings _saveSettings { get; set; } = new SaveSettings();
    public void OnLoadLocal(SaveSettings s) => _saveSettings = s;
    public SaveSettings OnSaveLocal() => _saveSettings;
        
    public const bool RemoveOrigNPCSounds = true;
    public AssetBundle audioBundle;
    public AudioSource audioSource;
    public Coroutine autoTextRoutine;
    internal static HKVocals instance;
    public bool ToggleButtonInsideMenu => false;
    public bool IsGrubRoom = false;
    public string GrubRoom = "Crossroads_48";
    public static NonBouncer CoroutineHolder;
    public static bool PlayDNInFSM = true;
    private GameObject lastDreamnailedEnemy;
    public static bool DidPlayAudioOnDialogueBox = false;

    private Regex enemyTrimRegex;

    public HKVocals() : base("Hallownest Vocalized")
    {
        var go = new GameObject("HK Vocals Coroutine Holder");
        CoroutineHolder = go.AddComponent<NonBouncer>();
        Object.DontDestroyOnLoad(CoroutineHolder);

        enemyTrimRegex = new Regex("([^0-9\\(\\)]+)", RegexOptions.Compiled);
    }
    public override string GetVersion() => "0.0.0.1";

    public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates) => ModMenu.CreateModMenuScreen(modListMenu);
        
    public override void Initialize()
    {
        instance = this;
        On.DialogueBox.ShowPage += PlayNPCDialogue;
        On.DialogueBox.HideText += StopAudioOnDialogueBoxClose;
        On.PlayMakerFSM.Awake += AddFSMEdits;
        On.HutongGames.PlayMaker.Actions.AudioPlayerOneShot.DoPlayRandomClip += PlayRandomClip;
        On.PlayMakerFSM.OnEnable += EasterEggs.SpecialGrub.EditSpecialGrub;
        On.EnemyDreamnailReaction.Start += EDNRStart;
        On.EnemyDreamnailReaction.ShowConvo += ShowConvo;
        On.HealthManager.TakeDamage += TakeDamage;
        OnAnimatorSequence.AfterOrig.Begin += MonomonIntro;
        OnAnimatorSequence.WithOrig.Skip += LockScrollIntro;
        On.ChainSequence.Update += ChainSequenceOnUpdate;
        UIManager.EditMenus += ModMenu.AddAudioSlider;

        ModHooks.LanguageGetHook += LanguageGet;
        ModHooks.LanguageGetHook += EasterEggs.SpecialGrub.GetSpecialGrubDialogue;
        ModHooks.LanguageGetHook += ElderbugAudioEdit;

        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += EasterEggs.EternalOrdeal.DeleteZoteAudioPlayersOnSceneChange;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += EasterEggs.ZoteLever.SetZoteLever;
        On.BossStatueLever.OnTriggerEnter2D += EasterEggs.ZoteLever.UseZoteLever;

        LoadAssetBundle();
        CreateAudioSource();
    }

    private string ElderbugAudioEdit(string key, string sheettitle, string orig)
    {
        if (key == "ELDERBUG_INTRO_MAIN_ALT" && sheettitle == "Elderbug")
        {
            orig = Language.Language.Get("ELDERBUG_INTRO_MAIN", sheettitle);
        }

        return orig;
    }

    private void LockScrollIntro(On.AnimatorSequence.orig_Skip orig, AnimatorSequence self)
    {
        if (_globalSettings.scrollLock)
        {
            return;
        }
        else
        {
            orig(self);
            audioSource.Stop();
        }
    }
    private void ChainSequenceOnUpdate(On.ChainSequence.orig_Update orig, ChainSequence self)
    {
        ChainSequenceR selfr = new(self);
        if (selfr.CurrentSequence != null && !selfr.CurrentSequence.IsPlaying && !selfr.isSkipped && AudioUtils.IsPlaying())
        {
            selfr.Next();
        }
    }
    private void MonomonIntro(OnAnimatorSequence.Delegates.Params_Begin args)
    {
        CoroutineHolder.StartCoroutine(WaitIg());
    }

    private IEnumerator WaitIg()
    {
        yield return null;
        yield return null;
        AudioUtils.TryPlayAudioFor("RANDOM_POEM_STUFF_0");
    }
    


    private void StopAudioOnDialogueBoxClose(On.DialogueBox.orig_HideText orig, DialogueBox self)
    {
        audioSource.Stop();
        orig.Invoke(self);
    }

    private void PlayNPCDialogue(On.DialogueBox.orig_ShowPage orig, DialogueBox self, int pageNum)
    {
        orig(self, pageNum);
        
        var convo = self.currentConversation + "_" + (self.currentPage - 1);

        float removeTime = self.currentPage - 1 == 0 ? 37f / 60f : 3f / 4f;

        bool audioPlayed = AudioUtils.TryPlayAudioFor(convo, removeTime);
        
     
        
        //this controls scroll lock and autoscroll
        if (audioPlayed)
        {
            DidPlayAudioOnDialogueBox = true;
        }
        else
        {
            DidPlayAudioOnDialogueBox = false;
        }
    }

    public void CreateAudioSource()
    {
        LogDebug("creating new asrc");
        GameObject audioGO = new GameObject("HK Vocals Audio");
        audioSource = audioGO.AddComponent<AudioSource>();
        Object.DontDestroyOnLoad(audioGO);
    }

    private void TakeDamage(On.HealthManager.orig_TakeDamage orig, HealthManager self, HitInstance hitInstance)
    {
        orig(self, hitInstance);
        for (int i = 0; i < Dictionaries.HpListeners.Count; i++)
        {
            if (Dictionaries.HpListeners[i](self))
            {
                Dictionaries.HpListeners.RemoveAt(i);
                i--;
            }
        }
    }

    public static string GetUniqueId(Transform transform, string path = "") {
        if (transform.parent == null) return $"{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}:" + path + transform.name;
        else return GetUniqueId(transform.parent, path + $"{transform.name}/");
    }

    private string LanguageGet(string key, string sheetTitle, string orig) {

        // Make sure this is dreamnail text
        if (lastDreamnailedEnemy == null) return orig;

        // Grab the ID and name now
        string id = GetUniqueId(lastDreamnailedEnemy.transform);
        string name = enemyTrimRegex.Match(lastDreamnailedEnemy.name).Value.Trim();

        // Prevent it from running again incorrectly
        lastDreamnailedEnemy = null;

        string group = key.Split('_')[0];

        // For the special case of grouped (generic) enemies
        if (DNAudios.DNGroups.ContainsKey(name)) name = DNAudios.DNGroups[name];

        List<string> availableClips = Dictionaries.audioNames.FindAll(s => s.Contains($"${name}$_{key}".ToUpper()));
        if (availableClips == null || availableClips.Count == 0) {
            LogError($"No clips for ${name}$_{key}");
            return orig;
        }

        // Either use the already registered VA or make one and save it
        int voiceActor;

        if (_saveSettings.PersistentVoiceActors.ContainsKey(id)) voiceActor = _saveSettings.PersistentVoiceActors[id];
        else {
            voiceActor = Random.Range(1, availableClips.Count);
            _saveSettings.PersistentVoiceActors[id] = voiceActor;
        }

        AudioUtils.TryPlayAudioFor($"${name}$_{key}_0_{voiceActor}".ToUpper());
        
        return orig;
    }

    private void ShowConvo(On.EnemyDreamnailReaction.orig_ShowConvo orig, EnemyDreamnailReaction self) {
        lastDreamnailedEnemy = self.gameObject;
        orig(self);
    }

    private void EDNRStart(On.EnemyDreamnailReaction.orig_Start orig, EnemyDreamnailReaction self)
    {
        if (self.gameObject.name == "Mace")
        {
            int rand = Random.Range(1, 10);
            if (rand == 10)
            {

            }
        }
        orig(self);
        //if (self.GetComponent<EnemyDeathEffects>() != null)
        self.gameObject.AddComponent<ExDNailReaction>();
    }

    private void PlayRandomClip(On.HutongGames.PlayMaker.Actions.AudioPlayerOneShot.orig_DoPlayRandomClip orig, AudioPlayerOneShot self)
    {
        orig(self);
        if (!RemoveOrigNPCSounds /*&& _globalSettings.testSetting == 0*/ && self.Fsm.Name == "Conversation Control")
        {
            HKVocals.CoroutineHolder.StartCoroutine(FadeOutClip(ReflectionHelper.GetField<AudioPlayerOneShot, AudioSource>(self, "audio")));
        }
    }

    private void AddFSMEdits(On.PlayMakerFSM.orig_Awake orig, PlayMakerFSM self)
    {
        orig(self);
        /*if (self.FsmGlobalTransitions.Any(t => t.EventName.ToLower().Contains("dream")))
        {
            self.MakeLog();
            foreach (FsmTransition t in self.FsmGlobalTransitions)
                Log(t.EventName);
        }*/
        if (Dictionaries.SceneFSMEdits.TryGetValue((UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, self.gameObject.name, self.FsmName), out var sceneAction))
            sceneAction(self);
        if (Dictionaries.GoFSMEdits.TryGetValue((self.gameObject.name, self.FsmName), out var goAction))
            goAction(self);
        if (Dictionaries.FSMChanges.TryGetValue(self.FsmName, out var action))
            action(self);

        /*if (self.gameObject.name.ToLower().Contains("elderbug"))
        {
            foreach (FsmVar v in self.FsmStates.SelectMany(s => s.Actions.Where(a => a is CallMethodProper call && call.behaviour.Value.ToLower() == "dialoguebox").Cast<CallMethodProper>().SelectMany(c => c.parameters)))
                Log(v.variableName + "  " + v.Type + "  " + v.GetValue());
        }*/
    }

   
    
    public void CreateDreamDialogue(string convName, string sheetName, string enemyType = "", string enemyVariant = "", GameObject enemy = null)
    {
        PlayMakerFSM fsm = FsmVariables.GlobalVariables.GetFsmGameObject("Enemy Dream Msg").Value.LocateMyFSM("Display");
        fsm.Fsm.GetFsmString("Convo Title").Value = convName;
        fsm.Fsm.GetFsmString("Sheet").Value = sheetName;
        fsm.SendEvent("DISPLAY DREAM MSG");
    }

    private IEnumerator FadeOutClip(AudioSource source)
    {
        float volumeChange = source.volume / 100f;
        yield return new WaitForSeconds(1f);
        for (int i = 0; i < 100; i++)
            source.volume -= volumeChange;
    }

    private void LoadAssetBundle()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        //audioBundle = AssetBundle.LoadFromStream(asm.GetManifestResourceStream("HKVocals.audiobundle"));
        audioBundle = AssetBundle.LoadFromStream(File.OpenRead(Path.GetDirectoryName(asm.Location) + "/audiobundle"));
        string[] allAssetNames = audioBundle.GetAllAssetNames();
        for (int i = 0; i < allAssetNames.Length; i++)
        {
            if (Dictionaries.audioExtentions.Any(ext => allAssetNames[i].EndsWith(ext)))
            {
                Dictionaries.audioNames.Add(Path.GetFileNameWithoutExtension(allAssetNames[i]).ToUpper());
            }
            LogDebug($"Object in audiobundle: {allAssetNames[i]} {Path.GetFileNameWithoutExtension(allAssetNames[i])?.ToUpper().Replace("KNGHT", "KNIGHT")}");
        }
    }
}
