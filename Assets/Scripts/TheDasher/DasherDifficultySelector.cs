using UnityEngine;
using UnityEngine.UI;

namespace Kinex.TheDasher
{
    /// <summary>
    /// Tiny UI-only helper for the 3 difficulty buttons (ง่าย / ปกติ / ยาก) on the TheDasher intro
    /// panel. Built and wired by TheDasherUIBuilder. Exists only because UnityEditor.Events.
    /// UnityEventTools has no persistent-listener overload that can pass an ENUM argument to a plain
    /// Button.onClick: AddIntPersistentListener needs a UnityAction&lt;int&gt;, and
    /// TheDasherDirector.SetDifficulty takes DasherDifficulty — C# won't convert that method group to
    /// UnityAction&lt;int&gt; (enum-to-int isn't a valid delegate conversion), so it can't be wired
    /// directly. Each button instead calls one of this component's plain void SelectX() methods via the
    /// normal UnityEventTools.AddPersistentListener(btn.onClick, selector.SelectX) pattern already used
    /// for every other button in TheDasherUIBuilder. This component also owns the "which one is
    /// selected" tint — simplest place for it since it already needs references to all 3 button Images.
    /// </summary>
    public class DasherDifficultySelector : MonoBehaviour
    {
        public TheDasherDirector director;
        public Image easyBg;
        public Image normalBg;
        public Image hardBg;
        public Color selectedColor = Color.white;
        public Color unselectedColor = new Color(0.65f, 0.65f, 0.65f, 0.7f);

        void Start() => ApplyInitialHighlight();

        /// <summary>
        /// Highlights whichever difficulty the director currently holds (Normal by default). Safe to
        /// call outside Play mode too — TheDasherUIBuilder calls this once right after wiring so the
        /// Scene view already shows the right button highlighted before anyone presses Play.
        /// </summary>
        public void ApplyInitialHighlight()
        {
            var d = director != null ? director.CurrentDifficulty : DasherDifficulty.Normal;
            Highlight(d);
        }

        public void SelectEasy() => Select(DasherDifficulty.Easy);
        public void SelectNormal() => Select(DasherDifficulty.Normal);
        public void SelectHard() => Select(DasherDifficulty.Hard);

        void Select(DasherDifficulty d)
        {
            if (director != null) director.SetDifficulty(d);
            Highlight(d);
        }

        void Highlight(DasherDifficulty d)
        {
            SetTint(easyBg, d == DasherDifficulty.Easy);
            SetTint(normalBg, d == DasherDifficulty.Normal);
            SetTint(hardBg, d == DasherDifficulty.Hard);
        }

        void SetTint(Image img, bool selected)
        {
            if (img != null) img.color = selected ? selectedColor : unselectedColor;
        }
    }
}
