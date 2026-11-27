using SpaceStation.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 12-3b 모듈 이동: 게임 씬 선택 패널(좌측 하단)에 [옮기기] 버튼을 만든다 — [철거] 버튼을 복제해 3번째 줄 [들어가기] 앞에 두고
    /// SelectionActionsPanel의 _moveButton · _moveLabel에 연결. 이미 연결돼 있으면 아무것도 하지 않는다.
    /// </summary>
    public static class MoveSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";

        [MenuItem("SpaceStation/Move/Setup Selection Button")]
        public static void Setup()
        {
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var panel = Object.FindFirstObjectByType<SelectionActionsPanel>(FindObjectsInactive.Include);
            if (panel == null)
            {
                Debug.LogError("[MoveSetup] SelectionActionsPanel 없음");
                return;
            }
            var so = new SerializedObject(panel);
            if (so.FindProperty("_moveButton").objectReferenceValue != null)
            {
                Debug.Log("[MoveSetup] 이미 연결됨");
                return;
            }
            var demolish = so.FindProperty("_demolishButton").objectReferenceValue as Button;
            var enter = so.FindProperty("_enterButton").objectReferenceValue as Button;
            if (demolish == null || enter == null)
            {
                Debug.LogError("[MoveSetup] 철거 · 들어가기 버튼이 연결돼 있지 않음");
                return;
            }

            var go = Object.Instantiate(demolish.gameObject, enter.transform.parent, false);
            go.name = "Move";
            go.transform.SetSiblingIndex(enter.transform.GetSiblingIndex());
            Undo.RegisterCreatedObjectUndo(go, "Move button");
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent(); // 클릭은 코드에서 연결
            var label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = "옮기기 (M)";

            so.FindProperty("_moveButton").objectReferenceValue = button;
            so.FindProperty("_moveLabel").objectReferenceValue = label;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MoveSetup] [옮기기] 버튼 추가 · 연결 완료");
        }

        /// <summary>
        /// CONNECT M7 묶음 선택: 선택 패널에 [가지 선택] 버튼 — [옮기기] 줄을 복제해 넷째 줄(Row4)로 두고 버튼 하나만 남겨
        /// SelectionActionsPanel의 _branchButton · _branchLabel에 연결. 이미 연결돼 있으면 아무것도 하지 않는다.
        /// </summary>
        [MenuItem("SpaceStation/Move/Setup Branch Button")]
        public static void SetupBranch()
        {
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var panel = Object.FindFirstObjectByType<SelectionActionsPanel>(FindObjectsInactive.Include);
            if (panel == null)
            {
                Debug.LogError("[MoveSetup] SelectionActionsPanel 없음");
                return;
            }
            var so = new SerializedObject(panel);
            if (so.FindProperty("_branchButton").objectReferenceValue != null)
            {
                Debug.Log("[MoveSetup] 가지 선택 버튼 이미 연결됨");
                return;
            }
            var move = so.FindProperty("_moveButton").objectReferenceValue as Button;
            if (move == null)
            {
                Debug.LogError("[MoveSetup] [옮기기] 버튼이 먼저 있어야 함 (Setup Selection Button)");
                return;
            }
            var row = move.transform.parent;
            var copy = Object.Instantiate(row.gameObject, row.parent, false);
            copy.name = "Row4";
            copy.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);
            Undo.RegisterCreatedObjectUndo(copy, "Branch row");
            Button branch = null;
            for (int i = copy.transform.childCount - 1; i >= 0; i--)
            {
                var child = copy.transform.GetChild(i);
                if (child.name == move.name && branch == null)
                {
                    branch = child.GetComponent<Button>();
                    child.name = "Branch";
                    continue;
                }
                Object.DestroyImmediate(child.gameObject);
            }
            if (branch == null)
            {
                Debug.LogError("[MoveSetup] 복제한 줄에서 [옮기기] 버튼을 못 찾음");
                return;
            }
            branch.onClick = new Button.ButtonClickedEvent(); // 클릭은 코드에서 연결
            var label = branch.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = "가지 선택 (G)";

            so.FindProperty("_branchButton").objectReferenceValue = branch;
            so.FindProperty("_branchLabel").objectReferenceValue = label;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MoveSetup] [가지 선택] 버튼 추가 · 연결 완료");
        }
    }
}
