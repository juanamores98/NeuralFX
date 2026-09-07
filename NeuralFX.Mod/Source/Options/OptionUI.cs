using System;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace NeuralFX.Options
{
    internal static class OptionUI
    {
        public static UIPanel GetPanel(UIHelperBase group)
        {
            if (group is UIHelper helper)
            {
                return helper.self as UIPanel;
            }
            return null;
        }

        public static UILabel AddLabel(UIHelperBase group, string text, Color32 color, float textScale = 0.85f)
        {
            var panel = GetPanel(group);
            if (panel == null) return null;

            var label = panel.AddUIComponent<UILabel>();
            label.text = text;
            label.textColor = color;
            label.textScale = textScale;
            label.processMarkup = true;
            label.autoSize = true;
            label.wordWrap = true;
            label.width = 620f;
            return label;
        }

        public static void AddStatusRow(UIHelperBase group, string title, string statusText, bool isGood, string description)
        {
            var panel = GetPanel(group);
            if (panel == null) return;

            // Row Container
            var container = panel.AddUIComponent<UIPanel>();
            container.width = 620f;
            container.autoLayout = true;
            container.autoLayoutDirection = LayoutDirection.Vertical;
            container.autoLayoutPadding = new RectOffset(0, 0, 1, 3);
            container.autoSize = true;

            // Header line: Title + Status Badge
            var header = container.AddUIComponent<UIPanel>();
            header.width = 620f;
            header.height = 24f;
            header.autoLayout = false;

            var titleLabel = header.AddUIComponent<UILabel>();
            titleLabel.text = title;
            titleLabel.textScale = 0.9f;
            titleLabel.textColor = new Color32(235, 235, 235, 255);
            titleLabel.autoSize = true;
            titleLabel.relativePosition = new Vector3(0, 3, 0);

            var statusBadge = header.AddUIComponent<UILabel>();
            string icon = isGood ? "● " : "○ ";
            statusBadge.text = icon + statusText;
            statusBadge.textScale = 0.85f;
            statusBadge.textColor = isGood ? new Color32(78, 201, 176, 255) : new Color32(230, 162, 60, 255); // Emerald vs Amber
            statusBadge.autoSize = true;
            statusBadge.relativePosition = new Vector3(250f, 4, 0);

            // Subtext Description
            if (!string.IsNullOrEmpty(description))
            {
                var descLabel = container.AddUIComponent<UILabel>();
                descLabel.text = description;
                descLabel.textScale = 0.78f;
                descLabel.textColor = new Color32(160, 160, 160, 255);
                descLabel.autoSize = true;
                descLabel.wordWrap = true;
                descLabel.width = 600f;
            }

            // Small spacing
            var spacer = container.AddUIComponent<UIPanel>();
            spacer.height = 4f;
            spacer.width = 620f;
        }

        public static void AddHint(UIHelperBase group, string hint)
        {
            AddLabel(group, hint, new Color32(140, 150, 160, 255), 0.78f);
        }
    }
}
