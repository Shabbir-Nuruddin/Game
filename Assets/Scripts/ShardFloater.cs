using UnityEngine;

namespace TrustIssues
{
    public class ShardFloater : MonoBehaviour
    {
        TextMesh _text;
        float _time;
        const float Life = 0.8f;

        public static void Spawn(Vector3 pos, int amount) => SpawnText(pos, "+" + amount, Theme.Coin);

        public static void SpawnText(Vector3 pos, string text, Color color)
        {
            var go = new GameObject("ShardFloater");
            Object.DontDestroyOnLoad(go);
            go.transform.position = pos + Vector3.up * 0.9f;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text; mesh.fontSize = 56; mesh.characterSize = 0.06f;
            mesh.fontStyle = FontStyle.Bold; mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center; mesh.color = color;
            go.GetComponent<MeshRenderer>().sortingOrder = 20;
            go.AddComponent<ShardFloater>()._text = mesh;
        }

        void Update()
        {
            _time += Time.unscaledDeltaTime;
            transform.position += Vector3.up * (1.1f * Time.unscaledDeltaTime);
            if (_text != null)
            {
                var color = _text.color; color.a = 1f - Mathf.Clamp01(_time / Life); _text.color = color;
            }
            if (_time >= Life) Destroy(gameObject);
        }
    }
}
