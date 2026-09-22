using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Trove.UI
{
    /// <summary>
    /// A small dark text panel on the large map, sized to its text and kept on screen. The text
    /// object is a clone of the map's biome label so it inherits the game's font and material.
    /// </summary>
    internal sealed class MapPanel
    {
        private RectTransform _panel;
        private TMP_Text _text;
        private string _lastText;

        public bool Created => _panel != null;

        public bool Create(Minimap map, string name)
        {
            if (_panel != null)
                return true;
            if (map == null || map.m_largeRoot == null || map.m_biomeNameLarge == null)
                return false;

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(map.m_largeRoot.transform, false);
            _panel = go.transform as RectTransform;
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.zero;
            _panel.pivot = new Vector2(0f, 1f);

            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.04f, 0.03f, 0.82f);
            bg.raycastTarget = false;

            GameObject textGo = Object.Instantiate(map.m_biomeNameLarge.gameObject, _panel);
            textGo.name = "Text";
            foreach (Component c in textGo.GetComponents<Component>())
                if (c != null && c.GetType().Name == "Localize")
                    Object.Destroy(c);
            _text = textGo.GetComponent<TMP_Text>();
            var trt = textGo.transform as RectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.pivot = new Vector2(0f, 1f);
            trt.offsetMin = new Vector2(10f, 7f);
            trt.offsetMax = new Vector2(-10f, -7f);
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.fontSize = 17f;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.richText = true;
            _text.color = Color.white;
            _text.raycastTarget = false;
            _text.text = "";

            go.SetActive(false);
            return true;
        }

        /// <summary>Show at a screen position, offset from it and flipped to stay inside the map root.</summary>
        public void ShowAtScreen(Minimap map, string content, Vector2 screen, float offset = 18f)
        {
            if (_panel == null)
                return;
            var root = map.m_largeRoot.transform as RectTransform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local))
                return;
            ShowAtRootLocal(map, content, local, offset);
        }

        /// <summary>Show beside a point given in the map root's local space.</summary>
        public void ShowAtRootLocal(Minimap map, string content, Vector2 local, float offset = 18f)
        {
            if (_panel == null)
                return;

            if (content != _lastText)
            {
                _lastText = content;
                _text.text = content;
                Vector2 size = _text.GetPreferredValues(content);
                _panel.sizeDelta = new Vector2(Mathf.Ceil(size.x) + 20f, Mathf.Ceil(size.y) + 14f);
            }

            var root = map.m_largeRoot.transform as RectTransform;
            Rect rr = root.rect;
            float x = local.x - rr.xMin + offset;
            float y = local.y - rr.yMin - offset;
            if (x + _panel.sizeDelta.x > rr.width)
                x = local.x - rr.xMin - offset - _panel.sizeDelta.x;
            if (y - _panel.sizeDelta.y < 0f)
                y = local.y - rr.yMin + offset + _panel.sizeDelta.y;
            _panel.anchoredPosition = new Vector2(x, y);

            if (!_panel.gameObject.activeSelf)
                _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
        }

        public void Hide()
        {
            if (_panel != null && _panel.gameObject.activeSelf)
                _panel.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (_panel != null)
                Object.Destroy(_panel.gameObject);
            _panel = null;
            _text = null;
            _lastText = null;
        }
    }
}
