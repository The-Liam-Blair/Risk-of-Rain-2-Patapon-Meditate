using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using RoR2;
using System.Reflection;
using UnityEngine;
using R2API.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Collections;
using UnityEngine.UI;
using RiskOfOptions;

namespace RiskOfPata
{
    [BepInPlugin(P_GUID, P_Name, P_Version)]

    [BepInDependency("com.rune500.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]

    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync)]

    // Main Plugin Class
    public class RiskOfPata : BaseUnityPlugin
    {
        // Plugin metadata and version
        public const string P_GUID = $"{P_Author}.{P_Name}";
        public const string P_Author = "RigsInRags";
        public const string P_Name = "PataponMeditation";
        public const string P_Version = "1.0.0";

        public static AssetBundle MainAssets;

        private PataponAnimator pataponAnimator;
        private GameObject wormFace;

        private float beatLifeTime;
        private int perfectBeats;

        public static ConfigEntry<bool> EnablePerfectBeatBonusDamage { get; set; }


        // todo: investigate NRE for "request list" on crosshairutils.crosshairoverridebehavior (NRE on behavior.ondestroy() ).
        // Error has not appeared in further testing, keeping this line incase it crops up again on an edge case.

        public void Awake()
        {
            DebugLog.Init(Logger);

            // Load the asset bundle for this mod.
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RiskOfPata.patassets"))
            {
                MainAssets = AssetBundle.LoadFromStream(stream);
            }

            EnablePerfectBeatBonusDamage = Config.Bind("Bonus Damage on Perfect Beat", "EnablePerfectBeatDamage", false, "Toggle for a +50% base damage buff for the meditate explosion when the meditate sequence is completed with all inputs being perfect beats");

            // Check for Risk of Options, if present setup the Risk of Options configs for this mod.
            if (RiskOfOptionsCompatibility.enabled)
            {
                RiskOfOptionsCompatibility.SetupRiskOfOptionsConfigs();
            }


            // Overrides the function that sets up the arrow icon sprites, replacing them with Patapon drum sprites.
            On.EntityStates.Seeker.MeditationUI.SetupInputUIIcons += (orig, self) =>
            {
                // For each generated input icon...
                for (int i = 0; i < 5; i++)
                {
                    // Associate each sequence icon with a drum of the equivalent input. (Up becomes Don, Right becomes Pon, etc.)
                    // This is done by replacing the sprite with the pressed drum's sprite.
                    switch (self.seekerController.meditationStepAndSequence[i + 1])
                    {
                        case 0:
                            self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[i]).GetComponent<Image>().sprite = MainAssets.LoadAsset<Sprite>("Chaka");
                            break;
                        case 1:
                            self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[i]).GetComponent<Image>().sprite = MainAssets.LoadAsset<Sprite>("Don");
                            break;
                        case 2:
                            self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[i]).GetComponent<Image>().sprite = MainAssets.LoadAsset<Sprite>("Pon");
                            break;
                        case 3:
                            self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[i]).GetComponent<Image>().sprite = MainAssets.LoadAsset<Sprite>("Pata");
                            break;
                    }

                    // Increase the scale of the icons as they're a bit small on default, and set the colour to white as it is changed repeatedly within the Meditation UI setup and update functions.
                    self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[i]).GetComponent<RectTransform>().localScale *= 1.75f;
                    self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[i]).GetComponent<Image>().color = UnityEngine.Color.white;

                    var parent = self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[i]).parent;
                    var icon = parent.Find(backdropString[i]).GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
                }

                // Removes the "background white" (Animated yellow bar) and "background black" (Very subtle black "outline") from view, only the visual timer remains viewable.
                self.overlayInstanceChildLocator.FindChild("MeditationBarTimer").parent.Find("BackgroundWhite").GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
                self.overlayInstanceChildLocator.FindChild("MeditationBarTimer").parent.Find("BackgroundBlack").GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

                var timerVisual = self.overlayInstanceChildLocator.FindChild("MeditationBarTimer").GetComponent<Image>();

                // Update the timer visual to use the worm sprite.
                timerVisual.GetComponent<Image>().sprite = MainAssets.LoadAsset<Sprite>("Worm2");

                // Flip it on the y axis to make it move left-to-right instead of the game's default right-to-left.
                timerVisual.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                // instantiate wormface ui prefab and attach to the parent.
                wormFace = UnityEngine.Object.Instantiate(MainAssets.LoadAsset<GameObject>("WormFace"), self.overlayInstanceChildLocator.FindChild("MeditationBarTimer").parent);
                wormFace.transform.localPosition = new Vector3(-505f, 0f, 0f);

                // Move the worm's head to the right at a constant speed equal to the meditation bar timer's speed.
                if (!pataponAnimator) { pataponAnimator = self.gameObject.AddComponent<PataponAnimator>(); }
                pataponAnimator.StartCoroutine(MoveWormHead(105f, wormFace.GetComponent<RectTransform>()));

                // If increased damage on perfect inputs is enabled...
                if (EnablePerfectBeatBonusDamage.Value)
                {
                    // Reset damage values to default, as these are modified if the user gets 4 perfect beats during skill execution.
                    self.damageCoefficient = 5f;
                }

                self.UpdateUIInputSequence();
            };


            // Adds additional functionality before and after the Update function, where the perfect beat timer is incremented and tracked,
            // handles the drum sprite fly and disappear animation on successful input, and sound effects for perfect and mistimed inputs.
            On.EntityStates.Seeker.MeditationUI.Update += (orig, self) =>
            {
                // Start recording the beat time once the user has started the input sequence.
                if (perfectBeats > 0)
                {
                    beatLifeTime += Time.deltaTime;
                }

                // Get the current step (Which input the user is currently on).
                var meditationStep = self.seekerController.meditationInputStep;

                // Let the update function run as normal.
                orig(self);

                // If the user has progressed onto the next input, animate the sprite to fly off the screen and disappear much like in the Patapon games.
                // Also plays the relevant sound effect.
                if (meditationStep != self.seekerController.meditationInputStep)
                {
                    self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[meditationStep]).GetComponent<RectTransform>().localScale *= 0.8f;

                    if (!pataponAnimator) { pataponAnimator = self.gameObject.AddComponent<PataponAnimator>(); }
                    pataponAnimator.StartCoroutine(NotesFlyAndDisappear(2f, self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[meditationStep])));


                    // If this is the user's first input, increment the counter by 1 to start the perfect beat process and play the perfect hit sound.
                    if (perfectBeats == 0)
                    {
                        perfectBeats++;

                        switch (self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[meditationStep]).GetComponent<Image>().sprite.name)
                        {
                            case "Pata":
                                Util.PlaySound("Pata", self.gameObject);
                                break;

                            case "Pon":
                                Util.PlaySound("Pon", self.gameObject);
                                break;

                            case "Chaka":
                                Util.PlaySound("Chaka", self.gameObject);
                                break;

                            case "Don":
                                Util.PlaySound("Don", self.gameObject);
                                break;
                        }

                    }

                    // If the user has started a perfect beat sequence and executed another perfect beat (+/- 0.75s from the central 0.5s mark),
                    // increment the perfect beat counter and play the relevant perfect hit sound.
                    else if (perfectBeats > 0 && beatLifeTime > 0.425f && beatLifeTime < 0.575f)
                    {
                        perfectBeats++;

                        switch (self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[meditationStep]).GetComponent<Image>().sprite.name)
                        {
                            case "Pata":
                                Util.PlaySound("Pata", self.gameObject);
                                break;

                            case "Pon":
                                Util.PlaySound("Pon", self.gameObject);
                                break;

                            case "Chaka":
                                Util.PlaySound("Chaka", self.gameObject);
                                break;

                            case "Don":
                                Util.PlaySound("Don", self.gameObject);
                                break;
                        }

                    }

                    // If the user didn't manage to hit a perfect beat, only play the relevant miss sound.
                    else
                    {
                        perfectBeats = 1;

                        switch (self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[meditationStep]).GetComponent<Image>().sprite.name)
                        {
                            case "Pata":
                                Util.PlaySound("PataMiss", self.gameObject);
                                break;

                            case "Pon":
                                Util.PlaySound("PonMiss", self.gameObject);
                                break;

                            case "Chaka":
                                Util.PlaySound("ChakaMiss", self.gameObject);
                                break;

                            case "Don":
                                Util.PlaySound("DonMiss", self.gameObject);
                                break;
                        }
                    }

                    beatLifeTime = 0f;

                    // If the user has accumulated 5 perfect beats (Every input was a perfect beat),
                    // Augment the meditation explosion to deal 50% more base damage.
                    if (perfectBeats >= 5)
                    {
                        // If higher damage on perfect beats is enabled, increase the damage coefficient to 7.5f. (+50% base damage).
                        if (EnablePerfectBeatBonusDamage.Value)
                        {
                            self.damageCoefficient = 7.5f;
                        }

                        // If all perfects, play the "cymbal" perfect hit sound (There exists 2 types, one for pata and chaka, another for pon and don)
                        // to indicate the user has achieved a perfect beat sequence.
                        switch (self.overlayInstanceChildLocator.FindChild(EntityStates.Seeker.MeditationUI.c[meditationStep]).GetComponent<Image>().sprite.name)
                        {
                            case "Pata":
                            case "Chaka":
                                Util.PlaySound("PataChakaSheen", self.gameObject);
                                break;

                            case "Pon":
                            case "Don":
                                Util.PlaySound("PonDonSheen", self.gameObject);
                                break;
                        }
                    }


                    // Swap the sprite's texture, which includes the combo counter, to show the relevant combo number.
                    // Combo number is based on the number of perfect beats the user has achieved in a row, as it resets on a mistimed input.
                    if (perfectBeats >= 2)
                    {
                        wormFace.GetComponent<Image>().sprite = MainAssets.LoadAsset<Sprite>("WormFaceC" + perfectBeats);
                    }

                    // If perfect beats are at 0 or 1, just load the default worm face sprite without the combo number.
                    else
                    {
                        wormFace.GetComponent<Image>().sprite = MainAssets.LoadAsset<Sprite>("WormFace");

                    }
                }

                // If user input an incorrect key for the current step, translate the worm face to the right to mimic the
                // meditation bar timer moving to the right from the time penalty.
                else if (meditationStep == self.seekerController.meditationInputStep &&
                self.inputBank.rawMoveDown.justPressed || self.inputBank.rawMoveUp.justPressed
                || self.inputBank.rawMoveLeft.justPressed || self.inputBank.rawMoveRight.justPressed)
                {
                    if (wormFace) { wormFace.GetComponent<RectTransform>().localPosition += new Vector3(105f, 0f, 0f); }
                }
            };


            // This hook just removes the implementation, as this function changes the sprite's color based on the input sequence.
            // This keeps the sprites white during the entire process.
            On.EntityStates.Seeker.MeditationUI.UpdateUIInputSequence += (orig, self) =>
            {
            };


            // IL Hook onto the MeditationUI's update function to set certain sound strings to empty strings.
            // This stops the game from playing those sounds (Util.PlaySound() catches empty strings) so the custom sounds will be the only ones played.
            IL.EntityStates.Seeker.MeditationUI.OnEnter += (il) =>
            {
                ILCursor c = new ILCursor(il);

                // Successful input press sound.
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldstr, "");
                c.Emit(OpCodes.Stfld, typeof(EntityStates.Seeker.MeditationUI).GetFieldCached("inputCorrectSoundString"));

                // Unsuccessful input press sound.
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldstr, "");
                c.Emit(OpCodes.Stfld, typeof(EntityStates.Seeker.MeditationUI).GetFieldCached("inputFailSoundString"));

                // Meditation completely successfully sound.
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldstr, "");
                c.Emit(OpCodes.Stfld, typeof(EntityStates.Seeker.MeditationUI).GetFieldCached("successSoundString"));

                // Meditation failed sound.
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldstr, "");
                c.Emit(OpCodes.Stfld, typeof(EntityStates.Seeker.MeditationUI).GetFieldCached("timeoutSoundString"));

                // Meditation started sound.
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldstr, "");
                c.Emit(OpCodes.Stfld, typeof(EntityStates.Seeker.MeditationUI).GetFieldCached("startSoundString"));
            };


            // Overrides the function that randomises the input sequence, now only randomising the first input
            // and replacing the final 4 inputs with a Patapon command sequence.
            On.RoR2.SeekerController.RandomizeMeditationInputs += (orig, self) =>
            {
                // 0 - chaka
                // 1 - don
                // 2 - pon
                // 3 - pata

                List<sbyte[]> sequences = new List<sbyte[]>
                {
                    new sbyte[4] { 3, 3, 3, 2 }, // March
                    new sbyte[4] { 2, 2, 3, 2 }, // Attack
                    new sbyte[4] { 0, 0, 3, 2 }, // Defend
                    new sbyte[4] { 2, 2, 0, 0 }, // Charge
                    new sbyte[4] { 1, 1, 0, 0 }, // Jump
                    new sbyte[4] { 2, 3, 2, 3 }, // Run
                    new sbyte[4] { 3, 2, 1, 0 }, // Party
                    new sbyte[4] { 0, 3, 0, 3 }, // Reverse
                };

                // First sequence element is 0, idk why but thats whats done in the original function.
                // This is "hidden" from the 5 input sequence, which is why the sequence array is 6 elements long.
                sbyte[] sequence = new sbyte[6];
                sequence[0] = 0;

                // Because patapon commands are 4 inputs long, just randomize the first input.
                sequence[1] = (sbyte)UnityEngine.Random.Range(0, 4);

                // Choose a random sequence from the command list and copy it to the sequence array, filling the last 4 elements.
                sequences[UnityEngine.Random.Range(0, sequences.Count)].CopyTo(sequence, 2);

                self.meditationStepAndSequence = sequence;

                self.CallCmdUpdateMeditationInput(self.meditationStepAndSequence);

                // Init perfect beat variables for reuse.
                perfectBeats = 0;
                beatLifeTime = 0f;
            };
        }

        /// <summary>
        /// Animate the drum sprites when they're pressed, flying in a given direction and quickly disappearing.
        /// </summary>
        /// <param name="duration">Duration of the entire effect in seconds.</param>
        /// <param name="note">The string name of the sprite (This is to control what direction the sprite moves by its drum and the intensity,
        ///                    matching how the Patapon games animate each drum's pressed sprite.</param>
        public IEnumerator NotesFlyAndDisappear(float duration, Transform note)
        {
            float timeElapsed = 0f;

            float xMove = 0f;
            float yMove = 0f;
            Color invisColour = new Color(1f, 1f, 1f, 0f);

            // Note: These were just estimates of the movement values based on comparing the animations made here to the ones observed
            //       in the Patapon games. They probably aren't extremely accurate but should be similar at a glance.
            switch (note.GetComponent<Image>().sprite.name)
            {
                // Pata drum: Moves rapidly to the left and up between slightly or a lot.
                case "Pata":
                    xMove = UnityEngine.Random.Range(-300f, -100f);
                    yMove = UnityEngine.Random.Range(-50f, 200f);
                    break;

                // Pon drum: Moves rapidly to the right and up between slightly or a lot. Basically opposite of Pata.
                case "Pon":
                    xMove = UnityEngine.Random.Range(100f, 300f);
                    yMove = UnityEngine.Random.Range(-50f, 200f);
                    break;

                // Chaka drum: Rapidly moves upwards. Can move left or right slightly.
                case "Chaka":
                    xMove = UnityEngine.Random.Range(-100f, 100f);
                    yMove = UnityEngine.Random.Range(200f, 350f);
                    break;

                // Don drum: Moves very slightly upwards, and to the left or right somewhat.
                case "Don":
                    xMove = UnityEngine.Random.Range(-150f, 150f);
                    yMove = UnityEngine.Random.Range(50f, 150f);
                    break;

                default:
                    DebugLog.Log("Invalid sprite name: " + note.GetComponent<Image>().sprite.name);
                    break;
            }

            var rect = note.GetComponent<RectTransform>();

            Vector3 finalScale = rect.localScale * 3f;

            while (timeElapsed < duration)
            {
                timeElapsed += Time.deltaTime;
                if (!rect) { yield break; }

                // Translates the sprite, using the vector calculated above, over time.
                rect.anchoredPosition += new Vector2(xMove * Time.deltaTime, yMove * Time.deltaTime);

                // Increases the size of the sprite over time, up to 50% larger after the full duration.
                rect.localScale = Vector3.Lerp(note.localScale, finalScale, timeElapsed / duration);

                // Fades the sprite out over time, becoming invisible after the full duration.
                note.GetComponent<Image>().color = Color.Lerp(note.GetComponent<Image>().color, invisColour, timeElapsed / duration);

                yield return null;
            }
        }

        /// <summary>
        /// Moves the "Worm Head" UI Sprite at the same speed as the Seeker's meditation bar timer to complete the animation.
        /// </summary>
        /// <param name="moveSpeed">Right-ward speed scalar of the head.</param>
        /// <param name="wormHead">Rect transform of the head.</param>
        /// <param name="duration">Maximum duration of the meditate skill.</param>
        public IEnumerator MoveWormHead(float moveSpeed, RectTransform wormHead, float duration = 5f)
        {
            float timeElapsed = 0f;

            while (timeElapsed < duration)
            {
                if (!wormHead) { yield break; }

                timeElapsed += Time.deltaTime;

                wormHead.anchoredPosition += new Vector2(moveSpeed * Time.deltaTime, 0f);

                yield return null;
            }
        }


        // List of strings for the existing "backdrops" objects (The circular objects that surrounded the arrow key inputs for the standard Seeker UI).
        // I'd rather do this iteratively but the naming convention used was inconsistent.
        private static string[] backdropString = new string[]
        {
            "InputBackDrop1",
            "InputBackDrop2",
            "InputBackDrop", // Huh
            "InputBackDrop1 (3)", // ?
            "InputBackDrop1 (2)" // why
        };
    }


    public class PataponAnimator : MonoBehaviour
    {
        private static PataponAnimator _instance;

        public static PataponAnimator instance
        {
            get
            {
                if (!_instance)
                {
                    GameObject obj = new GameObject("PataponAnimator");
                    _instance = obj.AddComponent<PataponAnimator>();
                }
                return _instance;
            }
        }
    }
}