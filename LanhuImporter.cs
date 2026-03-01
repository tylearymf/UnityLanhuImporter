using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;

namespace Lanhu
{
    public class LanhuImporter : EditorWindow
    {
        #region Fields & Constants

        ImportMode importMode;
        string jsonUrl = "";
        string jsonFilePath = "";

        float scaleFactor = 2;
        float targetWidth = 1920;
        bool useTargetWidth = false;

        List<string> publicModulePaths = new();
        string currentModulePath = "";
        List<string> fontDirPaths = new();
        List<string> externalUIDirPaths = new();
        string spriteOutputSuffix = "";
        string imageCacheDirPath = "Assets/TempImages";

        // Odin reorderable list trees
        PropertyTree publicModuleTree;
        PropertyTree fontDirTree;
        PropertyTree externalUITree;

        string lanhuCookie = "";
        string projectId = "";
        string imageId = "";
        string teamId = "";
        List<LanhuImageItem> projectImages = new();
        string selectedImageId = "";
        string selectedImageName = "";
        string imageSearchFilter = "";
        Vector2 imageListScrollPos;
        Vector2 mainScrollPos;
        int selectedTab;
        int sortColumn;
        bool sortAscending = true;

        static readonly string[] TabLabels = { "转换设置", "路径配置" };

        static readonly Color ColorDataSource = new(0.22f, 0.50f, 0.82f);
        static readonly Color ColorScale = new(0.30f, 0.65f, 0.35f);
        static readonly Color ColorModule = new(0.82f, 0.52f, 0.18f);
        static readonly Color ColorFont = new(0.58f, 0.38f, 0.78f);
        static readonly Color ColorExternalUI = new(0.20f, 0.62f, 0.62f);

        const string PublicModulesPrefsKey = "LanhuImporter_PublicModules";
        const string FontDirsPrefsKey = "LanhuImporter_FontDirs";
        const string CookiePrefsKey = "LanhuImporter_Cookie";
        const string ExternalUIDirPrefsKey = "LanhuImporter_ExternalUIDir";
        const string SpriteOutputPrefsKey = "LanhuImporter_SpriteOutput";
        const string ImageCacheDirPrefsKey = "LanhuImporter_ImageCacheDir";

        #endregion

        #region Editor Window Lifecycle

        [MenuItem("Tools/蓝湖UI转换器", priority = 9999)]
        public static void ShowWindow()
        {
            GetWindow<LanhuImporter>("蓝湖转换").Show();
        }

        void OnEnable()
        {
            var saved = EditorPrefs.GetString(PublicModulesPrefsKey, "");
            publicModulePaths.Clear();
            if (!string.IsNullOrEmpty(saved))
                publicModulePaths.AddRange(saved.Split('|', StringSplitOptions.RemoveEmptyEntries));

            var savedFonts = EditorPrefs.GetString(FontDirsPrefsKey, "");
            fontDirPaths.Clear();
            if (!string.IsNullOrEmpty(savedFonts))
                fontDirPaths.AddRange(savedFonts.Split('|', StringSplitOptions.RemoveEmptyEntries));

            lanhuCookie = EditorPrefs.GetString(CookiePrefsKey, "");

            var savedExternal = EditorPrefs.GetString(ExternalUIDirPrefsKey, "");
            externalUIDirPaths.Clear();
            if (!string.IsNullOrEmpty(savedExternal))
                externalUIDirPaths.AddRange(savedExternal.Split('|', StringSplitOptions.RemoveEmptyEntries));

            spriteOutputSuffix = EditorPrefs.GetString(SpriteOutputPrefsKey, "");
            imageCacheDirPath = EditorPrefs.GetString(ImageCacheDirPrefsKey, "Assets/TempImages");

            publicModuleTree = CreatePathListTree(publicModulePaths, SavePublicModules);
            fontDirTree = CreatePathListTree(fontDirPaths, SaveFontDirs);
            externalUITree = CreatePathListTree(externalUIDirPaths, SaveExternalUIDirs);
        }

        void OnDisable()
        {
            publicModuleTree?.Dispose();
            fontDirTree?.Dispose();
            externalUITree?.Dispose();
        }

        #endregion

        #region GUI Drawing

        void OnGUI()
        {
            mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos);
            EditorGUILayout.Space(4);

            selectedTab = GUILayout.Toolbar(selectedTab, TabLabels, GUILayout.Height(26));
            EditorGUILayout.Space(6);

            if (selectedTab == 0)
            {
                DrawDataSourceSection();
                EditorGUILayout.Space(6);
                DrawScaleSection();
                EditorGUILayout.Space(6);
                DrawModuleSection();
                EditorGUILayout.Space(12);
                DrawActionButton();
            }
            else
            {
                DrawPublicModulesSection();
                EditorGUILayout.Space(6);
                DrawFontSection();
                EditorGUILayout.Space(6);
                DrawExternalUISection();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.EndScrollView();
        }

        static void DrawSectionHeader(string title, Color color)
        {
            var rect = EditorGUILayout.GetControlRect(false, 22);
            EditorGUI.DrawRect(rect, color);
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white },
                fontSize = 12,
                padding = new RectOffset(8, 0, 0, 0)
            };
            GUI.Label(rect, title, style);
        }

        class PathListWrapper
        {
            [ListDrawerSettings(DraggableItems = true, HideAddButton = true, Expanded = true, ShowItemCount = false)]
            [HideLabel]
            public List<string> Paths;
        }

        PropertyTree CreatePathListTree(List<string> paths, Action saveAction)
        {
            var tree = PropertyTree.Create(new PathListWrapper { Paths = paths });
            tree.OnPropertyValueChanged += (_, _) => saveAction();
            return tree;
        }

        void DrawDataSourceSection()
        {
            SirenixEditorGUI.BeginBox();
            DrawSectionHeader("数据源设置", ColorDataSource);
            EditorGUILayout.Space(4);

            importMode = (ImportMode)EditorGUILayout.EnumPopup("导入模式", importMode);

            if (importMode == ImportMode.JsonUrl)
            {
                EditorGUILayout.Space(2);
                jsonUrl = EditorGUILayout.TextField("蓝湖 URL", jsonUrl);

                EditorGUILayout.BeginHorizontal();
                lanhuCookie = EditorGUILayout.TextField("Cookie (可选)", lanhuCookie);
                if (GUILayout.Button("保存", GUILayout.Width(40)))
                    EditorPrefs.SetString(CookiePrefsKey, lanhuCookie);
                EditorGUILayout.EndHorizontal();

                var isDirectJson = jsonUrl.Contains("SketchJSONURL");
                if (!isDirectJson)
                {
                    EditorGUILayout.Space(4);
                    var oldBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
                    if (GUILayout.Button("获取项目界面列表并选中目标界面"))
                        FetchProjectInfo();
                    GUI.backgroundColor = oldBg;

                    DrawImageListPanel();
                }
            }
            else
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                jsonFilePath = EditorGUILayout.TextField("JSON 文件", jsonFilePath);
                if (GUILayout.Button("浏览", GUILayout.Width(50)))
                {
                    var path = EditorUtility.OpenFilePanel("选择 JSON 文件", "", "");
                    if (!string.IsNullOrEmpty(path)) jsonFilePath = path;
                }
                EditorGUILayout.EndHorizontal();
            }

            SirenixEditorGUI.EndBox();
        }

        void DrawImageListPanel()
        {
            if (projectImages.Count <= 0) return;

            const float timeColWidth = 100f;

            EditorGUILayout.Space(4);
            SirenixEditorGUI.BeginBox();
            GUILayout.Label($"界面列表 ({projectImages.Count})", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            imageSearchFilter = SirenixEditorGUI.ToolbarSearchField(imageSearchFilter);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawSortColumnHeader("名称", 0);
            DrawSortColumnHeader("创建时间", 1, timeColWidth);
            DrawSortColumnHeader("更新时间", 2, timeColWidth);
            EditorGUILayout.EndHorizontal();

            imageListScrollPos = EditorGUILayout.BeginScrollView(imageListScrollPos, GUILayout.MaxHeight(200));
            foreach (var img in projectImages)
            {
                if (!string.IsNullOrEmpty(imageSearchFilter) &&
                    img.name.IndexOf(imageSearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var selected = selectedImageId == img.id;
                var rect = EditorGUILayout.GetControlRect(false, 20);
                var nameWidth = rect.width - timeColWidth * 2;

                if (selected)
                    EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.78f, 0.3f));

                if (selected)
                    GUI.Label(new Rect(rect.x, rect.y, 16, rect.height), "►");

                var nameRect = new Rect(rect.x + 16, rect.y, nameWidth - 16, rect.height);
                if (GUI.Button(nameRect, img.name, selected ? EditorStyles.boldLabel : EditorStyles.label))
                {
                    selectedImageId = img.id;
                    selectedImageName = img.name;
                }

                if (GUI.Button(new Rect(rect.x + nameWidth, rect.y, timeColWidth, rect.height),
                    FormatBeijingTime(img.create_time), EditorStyles.miniLabel))
                {
                    selectedImageId = img.id;
                    selectedImageName = img.name;
                }

                if (GUI.Button(new Rect(rect.x + nameWidth + timeColWidth, rect.y, timeColWidth, rect.height),
                    FormatBeijingTime(img.update_time), EditorStyles.miniLabel))
                {
                    selectedImageId = img.id;
                    selectedImageName = img.name;
                }
            }
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(selectedImageId))
                EditorGUILayout.HelpBox($"已选中: {selectedImageName}", MessageType.Info);

            SirenixEditorGUI.EndBox();
        }

        void DrawSortColumnHeader(string label, int column, float width = 0)
        {
            var arrow = sortColumn == column ? (sortAscending ? " ▲" : " ▼") : "";
            var style = new GUIStyle(EditorStyles.toolbarButton)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = sortColumn == column ? FontStyle.Bold : FontStyle.Normal
            };

            var clicked = width > 0
                ? GUILayout.Button(label + arrow, style, GUILayout.Width(width))
                : GUILayout.Button(label + arrow, style);

            if (clicked)
            {
                if (sortColumn == column)
                    sortAscending = !sortAscending;
                else
                {
                    sortColumn = column;
                    sortAscending = true;
                }
                SortProjectImages();
            }
        }

        void DrawScaleSection()
        {
            SirenixEditorGUI.BeginBox();
            DrawSectionHeader("比例设置", ColorScale);
            EditorGUILayout.Space(4);

            useTargetWidth = EditorGUILayout.Toggle("按指定宽度缩放", useTargetWidth);
            if (useTargetWidth)
                targetWidth = EditorGUILayout.FloatField("目标 UI 宽度", targetWidth);
            else
                scaleFactor = EditorGUILayout.FloatField("手动缩放倍数", scaleFactor);

            SirenixEditorGUI.EndBox();
        }

        void DrawModuleSection()
        {
            SirenixEditorGUI.BeginBox();
            DrawSectionHeader("模块设置", ColorModule);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            currentModulePath = EditorGUILayout.TextField("当前界面模块", currentModulePath);
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                var path = EditorUtility.OpenFolderPanel("选择当前界面模块目录", "Assets", "");
                if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
                    currentModulePath = "Assets" + path.Substring(Application.dataPath.Length);
            }
            EditorGUILayout.EndHorizontal();

            var newSuffix = EditorGUILayout.TextField("精灵导入目录后缀", spriteOutputSuffix);
            if (newSuffix != spriteOutputSuffix)
            {
                spriteOutputSuffix = newSuffix;
                EditorPrefs.SetString(SpriteOutputPrefsKey, spriteOutputSuffix);
            }

            if (!string.IsNullOrEmpty(currentModulePath) && !string.IsNullOrEmpty(spriteOutputSuffix))
                EditorGUILayout.HelpBox($"精灵导入目录: {currentModulePath}/{spriteOutputSuffix}", MessageType.None);

            SirenixEditorGUI.EndBox();
        }

        void DrawPublicModulesSection()
        {
            SirenixEditorGUI.BeginBox();
            DrawSectionHeader("公共模块目录", ColorModule);
            EditorGUILayout.Space(4);

            publicModuleTree.Draw(false);

            if (GUILayout.Button("+ 添加公共模块目录"))
            {
                var path = EditorUtility.OpenFolderPanel("选择公共模块目录", "Assets", "");
                if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
                {
                    publicModulePaths.Add("Assets" + path.Substring(Application.dataPath.Length));
                    SavePublicModules();
                }
            }

            SirenixEditorGUI.EndBox();
        }

        void DrawFontSection()
        {
            SirenixEditorGUI.BeginBox();
            DrawSectionHeader("字体目录", ColorFont);
            EditorGUILayout.Space(4);

            fontDirTree.Draw(false);

            if (GUILayout.Button("+ 添加字体目录"))
            {
                var path = EditorUtility.OpenFolderPanel("选择字体目录", "Assets", "");
                if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
                {
                    fontDirPaths.Add("Assets" + path.Substring(Application.dataPath.Length));
                    SaveFontDirs();
                }
            }

            SirenixEditorGUI.EndBox();
        }

        void DrawExternalUISection()
        {
            SirenixEditorGUI.BeginBox();
            DrawSectionHeader("外部UI目录", ColorExternalUI);
            EditorGUILayout.Space(4);

            externalUITree.Draw(false);

            if (GUILayout.Button("+ 添加外部UI目录"))
            {
                var path = EditorUtility.OpenFolderPanel("选择外部UI目录", "", "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    externalUIDirPaths.Add(path);
                    SaveExternalUIDirs();
                }
            }

            SirenixEditorGUI.EndBox();

            EditorGUILayout.Space(6);

            SirenixEditorGUI.BeginBox();
            DrawSectionHeader("其他设置", ColorExternalUI);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            var newCacheDir = EditorGUILayout.TextField("图片缓存目录", imageCacheDirPath);
            if (newCacheDir != imageCacheDirPath)
            {
                imageCacheDirPath = newCacheDir;
                EditorPrefs.SetString(ImageCacheDirPrefsKey, imageCacheDirPath);
            }
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                var path = EditorUtility.OpenFolderPanel("选择图片缓存目录", "Assets", "");
                if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
                {
                    imageCacheDirPath = "Assets" + path.Substring(Application.dataPath.Length);
                    EditorPrefs.SetString(ImageCacheDirPrefsKey, imageCacheDirPath);
                }
            }
            EditorGUILayout.EndHorizontal();

            SirenixEditorGUI.EndBox();
        }

        void DrawActionButton()
        {
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
            if (GUILayout.Button("开始转换并生成界面", GUILayout.Height(36)))
                StartProcess();
            GUI.backgroundColor = oldBg;
        }

        #endregion

        #region Processing

        void StartProcess()
        {
            try
            {
                var jsonContent = string.Empty;

                if (importMode == ImportMode.JsonUrl)
                {
                    var isDirectJson = jsonUrl.Contains("SketchJSONURL");
                    if (isDirectJson)
                    {
                        jsonContent = HttpGet(jsonUrl);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(selectedImageId))
                        {
                            Debug.LogError("[蓝湖转换] 请先获取项目列表并选择一个界面");
                            return;
                        }
                        jsonContent = FetchImageJsonContent(selectedImageId);
                    }
                    if (string.IsNullOrEmpty(jsonContent)) return;
                }
                else
                {
                    if (string.IsNullOrEmpty(jsonFilePath)) return;
                    jsonContent = System.IO.File.ReadAllText(jsonFilePath);
                }

                var v1Data = JsonUtility.FromJson<DataV1.LanhuBoardData>(jsonContent);
                var v2Data = JsonUtility.FromJson<DataV2.LanhuSketchData>(jsonContent);
                if (v1Data?.board?.layers != null && v1Data.board.layers.Count > 0)
                {
                    ProcessV1(v1Data);
                }
                else if (v2Data?.info != null && v2Data.info.Count > 0)
                {
                    ProcessV2(v2Data);
                }
                else
                {
                    Debug.LogError($"[蓝湖转换] 无法解析");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[蓝湖转换] 出错: {e.Message}");
            }
        }

        void ProcessV2(DataV2.LanhuSketchData sketchData)
        {
            var finalScale = scaleFactor;
            if (useTargetWidth && targetWidth > 0)
                finalScale = targetWidth / sketchData.info[0].width;

            DataV2.LanhuLayerInfo rootData = null;
            var map = new Dictionary<string, DataV2.LanhuLayerInfo>();
            foreach (var item in sketchData.info)
                map[item.id] = item;

            foreach (var item in sketchData.info)
            {
                if (string.IsNullOrEmpty(item.parentID) || !map.ContainsKey(item.parentID))
                {
                    rootData = item;
                    rootData.isRoot = true;
                }
                else
                {
                    map[item.parentID].children.Add(item);
                }
            }

            var canvases = FindObjectsOfType<Canvas>();
            var canvas = canvases.Length > 0 ? canvases[^1] : null;
            if (canvas == null) canvas = new GameObject("Canvas").AddComponent<Canvas>();
            var rootNode = CreateLayerNodeV2(rootData, canvas.transform, finalScale, rootData.width, rootData.height);
            WrapChildrenInGenerate(rootNode.transform);
            CreatePreviewNode(rootNode.transform);
            OptimizeTransform((RectTransform)rootNode.transform);

            Debug.Log($"UI生成完毕. name:{rootData.name}", rootNode);
        }

        void ProcessV1(DataV1.LanhuBoardData data)
        {
            var board = data.board;
            var artRect = board.artboard.artboardRect;
            var rootWidth = artRect.right - artRect.left;
            var rootHeight = artRect.bottom - artRect.top;

            var finalScale = scaleFactor;
            if (useTargetWidth && targetWidth > 0)
                finalScale = targetWidth / rootWidth;

            var canvases = FindObjectsOfType<Canvas>();
            var canvas = canvases.Length > 0 ? canvases[^1] : null;
            if (canvas == null) canvas = new GameObject("Canvas").AddComponent<Canvas>();

            var rootNode = new GameObject(board.name);
            var rootRt = rootNode.AddComponent<RectTransform>();
            rootRt.pivot = rootRt.anchorMax = rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(rootWidth * finalScale, rootHeight * finalScale);
            rootRt.anchoredPosition = Vector2.zero;
            rootNode.transform.SetParent(canvas.transform);

            if (board.layers != null)
            {
                foreach (var layer in board.layers)
                    CreateLayerNodeV1(layer, rootNode.transform, finalScale, rootWidth, rootHeight);
            }

            WrapChildrenInGenerate(rootNode.transform);
            CreatePreviewNode(rootNode.transform);
            OptimizeTransform(rootRt);
            Debug.Log($"[蓝湖转换] V1 UI生成完毕. name:{board.name}", rootNode);
        }

        #endregion

        #region Node Creation (V2)

        GameObject CreateLayerNodeV2(DataV2.LanhuLayerInfo info, Transform parent, float scale, float rootWidth, float rootHeight)
        {
            var node = new GameObject(info.name);
            var rt = node.AddComponent<RectTransform>();

            SetBaseTransform(rt, info, scale, rootWidth, rootHeight);

            if (parent != null) node.transform.SetParent(parent);

            rt.localEulerAngles = new Vector3(info.isFlippedVertical ? 180 : 0, info.isFlippedHorizontal ? 180 : 0, 0);

            // ─── 基础组件识别流水线 ───
            if (info.type == "text")
            {
                SetTextLayerV2(node, info, scale);
            }
            else if (IsImageComponent(info.name))
            {
                SetImageLayerV2(node, info, scale);
            }

            // 递归创建子节点
            if (info.children != null)
            {
                foreach (var child in info.children)
                    CreateLayerNodeV2(child, node.transform, scale, rootWidth, rootHeight);
            }

            // ─── 特殊组件识别 ───
            TryAddButton(node, info.name);
            TryAddGradientColor(node, info.fills);
            TryAddScrollRect(node);
            // [在此添加更多组件识别]

            return node;
        }

        void SetBaseTransform(RectTransform rt, DataV2.LanhuLayerInfo info, float scale, float rootWidth, float rootHeight)
        {
            rt.pivot = rt.anchorMax = rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(info.width * scale, info.height * scale);

            if (info.isRoot)
            {
                rt.anchoredPosition = Vector2.zero;
            }
            else
            {
                var posX = (info.left * scale) + (info.width * scale / 2f) - (rootWidth * scale / 2f);
                var posY = (rootHeight * scale / 2f) - (info.top * scale) - (info.height * scale / 2f);
                rt.anchoredPosition = new Vector2(posX, posY);
            }
        }

        void SetTextLayerV2(GameObject go, DataV2.LanhuLayerInfo info, float scale)
        {
            var textComp = go.AddComponent<Text>();
            if (info.font == null) return;

            textComp.raycastTarget = false;
            textComp.supportRichText = true;
            textComp.fontSize = Mathf.RoundToInt(info.font.size * scale);

            string fontName = null;
            if (info.font.styles != null && info.font.styles.Count > 0)
            {
                var firstStyle = info.font.styles[0];
                fontName = firstStyle.font;

                textComp.alignment = firstStyle.alignment switch
                {
                    0 => TextAnchor.MiddleLeft,
                    1 => TextAnchor.MiddleCenter,
                    2 => TextAnchor.MiddleRight,
                    _ => TextAnchor.MiddleCenter,
                };
            }

            if (string.IsNullOrEmpty(fontName))
                fontName = info.font.displayName;

            var foundFont = SearchFont(fontName);
            if (foundFont != null)
                textComp.font = foundFont;

            if (info.font.styles != null && info.font.styles.Count > 0)
            {
                if (info.font.styles.Count > 1)
                {
                    var richText = "";
                    foreach (var s in info.font.styles)
                    {
                        var hex = ColorUtility.ToHtmlStringRGBA(s.color);
                        var fontStyle = GetFontStyle(s.font);
                        if (fontStyle == FontStyle.Normal)
                            richText += $"<color=#{hex}>{s.content}</color>";
                        else
                            richText += $"<color=#{hex}><b>{s.content}</b></color>";
                    }
                    textComp.text = richText;
                }
                else
                {
                    textComp.text = info.font.content;
                    textComp.color = info.font.styles[0].color;
                    textComp.fontStyle = GetFontStyle(fontName);
                }
            }
            else
            {
                textComp.text = info.font.content;
            }

            textComp.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComp.verticalOverflow = VerticalWrapMode.Truncate;

            var settings = textComp.GetGenerationSettings(textComp.rectTransform.rect.size);
            var preferredW = textComp.cachedTextGeneratorForLayout.GetPreferredWidth(textComp.text, settings) / textComp.pixelsPerUnit;
            var preferredH = textComp.cachedTextGeneratorForLayout.GetPreferredHeight(textComp.text, settings) / textComp.pixelsPerUnit;
            var finalWidth = Mathf.Max(info.width * scale, preferredW) + 1f;
            var finalHeight = Mathf.Max(info.height * scale, preferredH) + 1f;

            textComp.rectTransform.sizeDelta = new Vector2(finalWidth, finalHeight);
        }

        void SetImageLayerV2(GameObject go, DataV2.LanhuLayerInfo info, float scale)
        {
            var img = go.AddComponent<Image>();
            img.raycastTarget = true;

            var sprite = SearchSprite(info.name);
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;

                if (sprite.border != Vector4.zero)
                    img.type = Image.Type.Sliced;

                if (info.fills != null && info.fills.Count > 0)
                {
                    var colorStyle = info.fills[0].color;
                    if (colorStyle != null)
                        img.color = colorStyle;
                }
                return;
            }
        }

        #endregion

        #region Node Creation (V1)

        GameObject CreateLayerNodeV1(DataV1.LanhuLayer layer, Transform parent, float scale, float rootWidth, float rootHeight)
        {
            var node = new GameObject(layer.name);
            var rt = node.AddComponent<RectTransform>();

            rt.pivot = rt.anchorMax = rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(layer.width * scale, layer.height * scale);

            var posX = (layer.left * scale) + (layer.width * scale / 2f) - (rootWidth * scale / 2f);
            var posY = (rootHeight * scale / 2f) - (layer.top * scale) - (layer.height * scale / 2f);
            rt.anchoredPosition = new Vector2(posX, posY);

            if (parent != null) node.transform.SetParent(parent);

            // ─── 基础组件识别流水线 ───
            if (layer.type == "textLayer")
            {
                SetTextLayerV1(node, layer, scale);
            }
            else if (IsImageComponent(layer.name))
            {
                SetImageLayerV1(node, layer, scale);
            }

            // 递归创建子节点
            if (layer.layers != null)
            {
                foreach (var child in layer.layers)
                    CreateLayerNodeV1(child, node.transform, scale, rootWidth, rootHeight);
            }

            // ─── 特殊组件识别 ───
            TryAddButton(node, layer.name);
            TryAddGradientColor(node, layer.fills);
            TryAddScrollRect(node);
            // [在此添加更多组件识别]

            return node;
        }

        void SetTextLayerV1(GameObject go, DataV1.LanhuLayer layer, float scale)
        {
            var textComp = go.AddComponent<Text>();
            var textInfo = layer.textInfo;
            if (textInfo == null) return;

            textComp.raycastTarget = false;
            textComp.supportRichText = true;
            textComp.fontSize = Mathf.RoundToInt(textInfo.size * scale);

            var fontName = textInfo.fontName;
            var foundFont = SearchFont(fontName);
            if (foundFont != null)
                textComp.font = foundFont;

            textComp.alignment = textInfo.justification switch
            {
                "left" => TextAnchor.MiddleLeft,
                "center" => TextAnchor.MiddleCenter,
                "right" => TextAnchor.MiddleRight,
                _ => TextAnchor.MiddleCenter,
            };

            if (textInfo.textStyleRange != null && textInfo.textStyleRange.Count > 1)
            {
                var richText = "";
                foreach (var range in textInfo.textStyleRange)
                {
                    var style = range.textStyle;
                    if (style == null) continue;

                    var from = range.from;
                    var to = range.to;
                    if (from >= textInfo.text.Length) continue;
                    if (to > textInfo.text.Length) to = textInfo.text.Length;

                    var segment = textInfo.text.Substring(from, to - from);
                    var c = style.color != null ? (Color)style.color : (textInfo.color != null ? (Color)textInfo.color : Color.white);
                    var hex = ColorUtility.ToHtmlStringRGBA(c);

                    var fontStyle = GetFontStyle(style.fontStyleName ?? style.fontName);
                    if (fontStyle == FontStyle.Bold)
                        richText += $"<color=#{hex}><b>{segment}</b></color>";
                    else
                        richText += $"<color=#{hex}>{segment}</color>";
                }
                textComp.text = richText;
            }
            else
            {
                textComp.text = textInfo.text;
                if (textInfo.color != null)
                    textComp.color = textInfo.color;
                textComp.fontStyle = GetFontStyle(textInfo.fontStyleName ?? fontName);
            }

            textComp.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComp.verticalOverflow = VerticalWrapMode.Truncate;

            var settings = textComp.GetGenerationSettings(textComp.rectTransform.rect.size);
            var preferredW = textComp.cachedTextGeneratorForLayout.GetPreferredWidth(textComp.text, settings) / textComp.pixelsPerUnit;
            var preferredH = textComp.cachedTextGeneratorForLayout.GetPreferredHeight(textComp.text, settings) / textComp.pixelsPerUnit;
            var finalWidth = Mathf.Max(layer.width * scale, preferredW) + 1f;
            var finalHeight = Mathf.Max(layer.height * scale, preferredH) + 1f;
            textComp.rectTransform.sizeDelta = new Vector2(finalWidth, finalHeight);
        }

        void SetImageLayerV1(GameObject go, DataV1.LanhuLayer layer, float scale)
        {
            var img = go.AddComponent<Image>();
            img.raycastTarget = true;

            var sprite = SearchSprite(layer.name);
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;

                if (sprite.border != Vector4.zero)
                    img.type = Image.Type.Sliced;

                return;
            }
        }

        #endregion

        #region Asset Search

        Sprite SearchSprite(string spriteName)
        {
            if (!string.IsNullOrEmpty(currentModulePath))
            {
                var sprite = SearchLooseSprite(spriteName, currentModulePath);
                if (sprite != null) return sprite;
            }

            foreach (var modulePath in publicModulePaths)
            {
                var sprite = SearchLooseSprite(spriteName, modulePath);
                if (sprite != null) return sprite;
            }

            foreach (var externalDir in externalUIDirPaths)
            {
                if (string.IsNullOrEmpty(externalDir)) continue;
                var isProjectInternal = externalDir.StartsWith("Assets");
                if (isProjectInternal)
                {
                    var sprite = SearchLooseSprite(spriteName, externalDir);
                    if (sprite != null)
                    {
                        var copied = CopySpriteToOutput(sprite);
                        if (copied != null) return copied;
                        return sprite;
                    }
                }
                else
                {
                    var copied = SearchExternalAndCopy(spriteName, externalDir);
                    if (copied != null) return copied;
                }
            }

            return null;
        }

        Sprite SearchLooseSprite(string spriteName, string folderPath)
        {
            var guids = AssetDatabase.FindAssets($"{spriteName} t:Sprite", new[] { folderPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var asset in assets)
                {
                    if (asset is Sprite s && s.name == spriteName)
                        return s;
                }
            }
            return null;
        }

        Font SearchFont(string fontName)
        {
            if (fontDirPaths.Count == 0) return null;
            if (string.IsNullOrEmpty(fontName)) return null;

            foreach (var dirPath in fontDirPaths)
            {
                var guids = AssetDatabase.FindAssets("t:Font", new[] { dirPath });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                    if (font != null && string.Equals(font.name, fontName, StringComparison.OrdinalIgnoreCase))
                        return font;
                }
            }

            foreach (var dirPath in fontDirPaths)
            {
                var guids = AssetDatabase.FindAssets("t:Font", new[] { dirPath });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                    if (font != null && font.name.IndexOf(fontName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return font;
                }
            }

            var guids2 = AssetDatabase.FindAssets("t:Font", fontDirPaths.ToArray());
            foreach (var guid in guids2)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (font != null) return font;
            }

            return null;
        }

        string GetSpriteOutputPath()
        {
            if (string.IsNullOrEmpty(currentModulePath) || string.IsNullOrEmpty(spriteOutputSuffix))
                return null;
            return $"{currentModulePath}/{spriteOutputSuffix}";
        }

        Sprite CopySpriteToOutput(Sprite sourceSprite)
        {
            var outputPath = GetSpriteOutputPath();
            if (string.IsNullOrEmpty(outputPath)) return null;

            var srcPath = AssetDatabase.GetAssetPath(sourceSprite);
            if (string.IsNullOrEmpty(srcPath)) return null;

            var fullOutputDir = System.IO.Path.Combine(Application.dataPath, outputPath.Substring("Assets/".Length));
            if (!System.IO.Directory.Exists(fullOutputDir))
                System.IO.Directory.CreateDirectory(fullOutputDir);

            var fileName = System.IO.Path.GetFileName(srcPath);
            var destPath = $"{outputPath}/{fileName}";

            if (AssetDatabase.LoadAssetAtPath<Sprite>(destPath) != null)
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath(destPath);
                foreach (var asset in assets)
                {
                    if (asset is Sprite s && s.name == sourceSprite.name)
                        return s;
                }
            }

            if (!AssetDatabase.CopyAsset(srcPath, destPath))
            {
                Debug.LogWarning($"[蓝湖转换] 拷贝图片失败: {srcPath} -> {destPath}");
                return null;
            }

            AssetDatabase.ImportAsset(destPath);

            var copiedAssets = AssetDatabase.LoadAllAssetsAtPath(destPath);
            foreach (var asset in copiedAssets)
            {
                if (asset is Sprite s && s.name == sourceSprite.name)
                    return s;
            }

            return null;
        }

        Sprite SearchExternalAndCopy(string spriteName, string externalDir)
        {
            var outputPath = GetSpriteOutputPath();
            if (string.IsNullOrEmpty(outputPath)) return null;
            if (!System.IO.Directory.Exists(externalDir)) return null;

            var extensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.psd", "*.bmp" };
            foreach (var ext in extensions)
            {
                var files = System.IO.Directory.GetFiles(externalDir, ext, System.IO.SearchOption.AllDirectories);
                foreach (var filePath in files)
                {
                    var fileNameNoExt = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    if (!string.Equals(fileNameNoExt, spriteName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fullOutputDir = System.IO.Path.Combine(Application.dataPath, outputPath.Substring("Assets/".Length));
                    if (!System.IO.Directory.Exists(fullOutputDir))
                        System.IO.Directory.CreateDirectory(fullOutputDir);

                    var fileName = System.IO.Path.GetFileName(filePath);
                    var destPath = $"{outputPath}/{fileName}";
                    var fullDestPath = System.IO.Path.Combine(Application.dataPath, destPath.Substring("Assets/".Length));

                    if (System.IO.File.Exists(fullDestPath))
                    {
                        var assets = AssetDatabase.LoadAllAssetsAtPath(destPath);
                        foreach (var asset in assets)
                        {
                            if (asset is Sprite s && s.name == spriteName)
                                return s;
                        }
                    }

                    System.IO.File.Copy(filePath, fullDestPath, true);
                    AssetDatabase.ImportAsset(destPath);

                    var imported = AssetDatabase.LoadAllAssetsAtPath(destPath);
                    foreach (var asset in imported)
                    {
                        if (asset is Sprite s && s.name == spriteName)
                            return s;
                    }

                    foreach (var asset in imported)
                    {
                        if (asset is Sprite s)
                            return s;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Component Recognition
        // 组件识别方法，按流水线调用顺序排列。

        /// <summary>
        /// 按钮识别: 节点名称包含 btn 关键字时添加 Button 组件。
        /// </summary>
        void TryAddButton(GameObject go, string name)
        {
            if (name.IndexOf("btn", StringComparison.OrdinalIgnoreCase) < 0) return;

            if (!go.TryGetComponent<Image>(out var img))
            {
                return;
            }

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
        }

        /// <summary>
        /// 渐变色识别: fills[0].gradient 有数据时添加 GradientColor 组件。
        /// 注意: GradientColor 是当前项目特有的脚本（位于 Assembly-CSharp），此处通过反射添加。
        /// </summary>
        void TryAddGradientColor(GameObject go, List<DataV2.LanhuFillStyle> fills)
        {
            if (!go.TryGetComponent<Image>(out var img)) return;
            if (fills == null || fills.Count == 0) return;
            var gradient = fills[0].gradient;
            if (gradient == null || gradient.colorStops == null || gradient.colorStops.Count < 2) return;

            var gradientColorType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "GradientColor");

            if (gradientColorType == null)
            {
                Debug.LogWarning("[蓝湖转换] 未找到 GradientColor 脚本，跳过渐变设置");
                return;
            }

            var comp = go.AddComponent(gradientColorType);

            var firstColor = gradient.colorStops[0].color;
            var lastColor = gradient.colorStops[gradient.colorStops.Count - 1].color;

            var bottomColor = firstColor != null ? (Color)firstColor : Color.white;
            var topColor = lastColor != null ? (Color)lastColor : Color.white;

            gradientColorType.GetField("bottomColor").SetValue(comp, bottomColor);
            gradientColorType.GetField("topColor").SetValue(comp, topColor);

            if (gradient.from != null && gradient.to != null)
            {
                var dx = Mathf.Abs(gradient.from.x - gradient.to.x);
                var dy = Mathf.Abs(gradient.from.y - gradient.to.y);
                var directionType = gradientColorType.GetNestedType("Direction");
                var directionValue = dx >= dy
                    ? Enum.Parse(directionType, "Horizontal")
                    : Enum.Parse(directionType, "Vertical");
                gradientColorType.GetField("GradientType").SetValue(comp, directionValue);
            }
        }

        /// <summary>
        /// 滚动列表识别: 子节点宽高一致且按固定间距排列时添加 ScrollRect。
        /// </summary>
        void TryAddScrollRect(GameObject go)
        {
            if (!go.TryGetComponent<RectTransform>(out var rt)) return;

            var children = new List<RectTransform>();
            foreach (RectTransform child in rt)
            {
                if (child.name.StartsWith("_")) continue;
                children.Add(child);
            }

            if (children.Count < 2) return;

            // 检查所有子节点尺寸是否一致
            var refSize = children[0].sizeDelta;
            const float tolerance = 2f;
            foreach (var child in children)
            {
                if (Mathf.Abs(child.sizeDelta.x - refSize.x) > tolerance ||
                    Mathf.Abs(child.sizeDelta.y - refSize.y) > tolerance)
                    return;
            }

            // 判断排列方向: 水平 or 垂直
            var sortedByX = children.OrderBy(c => c.anchoredPosition.x).ToList();
            var sortedByY = children.OrderByDescending(c => c.anchoredPosition.y).ToList();
            bool isHorizontal = IsEvenlySpaced(sortedByX, true);
            bool isVertical = IsEvenlySpaced(sortedByY, false);

            if (!isHorizontal && !isVertical) return;

            // 创建 Content 容器
            var content = new GameObject("Content");
            var contentRt = content.AddComponent<RectTransform>();
            contentRt.SetParent(go.transform, false);
            contentRt.pivot = contentRt.anchorMin = contentRt.anchorMax = new Vector2(0.5f, 0.5f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = rt.sizeDelta;

            // 移动子节点到 Content
            foreach (var child in children)
                child.SetParent(contentRt, false);

            var scrollRect = go.AddComponent<ScrollRect>();
            scrollRect.content = contentRt;
            scrollRect.horizontal = isHorizontal;
            scrollRect.vertical = !isHorizontal && isVertical;
        }

        bool IsEvenlySpaced(List<RectTransform> sorted, bool horizontal)
        {
            if (sorted.Count < 2) return false;
            var spacings = new List<float>();
            for (int i = 1; i < sorted.Count; i++)
            {
                float diff = horizontal
                    ? sorted[i].anchoredPosition.x - sorted[i - 1].anchoredPosition.x
                    : sorted[i - 1].anchoredPosition.y - sorted[i].anchoredPosition.y;
                spacings.Add(diff);
            }
            var avg = spacings.Average();
            return avg > 0 && spacings.All(s => Mathf.Abs(s - avg) < 2f);
        }

        #endregion

        #region Preview & Utility

        /// <summary>
        /// 将根节点的所有子节点包裹到 _Generate 容器中，使根节点结构为: _Preview + _Generate。
        /// </summary>
        void WrapChildrenInGenerate(Transform rootTransform)
        {
            var rootRt = rootTransform.GetComponent<RectTransform>();

            var generateNode = new GameObject("_Generate");
            var generateRt = generateNode.AddComponent<RectTransform>();
            generateRt.SetParent(rootTransform, false);
            generateRt.pivot = generateRt.anchorMin = generateRt.anchorMax = new Vector2(0.5f, 0.5f);
            generateRt.anchoredPosition = Vector2.zero;
            generateRt.sizeDelta = rootRt != null ? rootRt.sizeDelta : Vector2.zero;

            var childrenToMove = new List<Transform>();
            foreach (Transform child in rootTransform)
            {
                if (child != generateNode.transform)
                    childrenToMove.Add(child);
            }
            foreach (var child in childrenToMove)
                child.SetParent(generateRt, false);
        }

        void CreatePreviewNode(Transform rootNode)
        {
            var selectedImg = projectImages.Find(x => x.id == selectedImageId);
            if (selectedImg == null || string.IsNullOrEmpty(selectedImg.url)) return;
            if (string.IsNullOrEmpty(imageCacheDirPath)) return;

            var relativePath = imageCacheDirPath.StartsWith("Assets/")
                ? imageCacheDirPath.Substring("Assets/".Length)
                : imageCacheDirPath;
            var fullCacheDir = System.IO.Path.Combine(Application.dataPath, relativePath);
            if (!System.IO.Directory.Exists(fullCacheDir))
                System.IO.Directory.CreateDirectory(fullCacheDir);

            var fileName = Regex.Replace($"{selectedImg.name}.png", @"[<>:""/\\|?*]", "_");
            var assetPath = $"{imageCacheDirPath}/{fileName}";
            var fullPath = System.IO.Path.Combine(Application.dataPath,
                assetPath.StartsWith("Assets/") ? assetPath.Substring("Assets/".Length) : assetPath);

            using (var www = UnityWebRequest.Get(selectedImg.url))
            {
                if (!string.IsNullOrEmpty(lanhuCookie))
                    www.SetRequestHeader("Cookie", lanhuCookie);
                www.SendWebRequest();
                while (!www.isDone) { }
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[蓝湖转换] 下载预览图失败: {www.error}");
                    return;
                }
                System.IO.File.WriteAllBytes(fullPath, www.downloadHandler.data);
            }

            AssetDatabase.ImportAsset(assetPath);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null) return;

            var rootRt = rootNode.GetComponent<RectTransform>();
            var previewObj = new GameObject("_Preview");
            var previewRt = previewObj.AddComponent<RectTransform>();
            previewRt.pivot = previewRt.anchorMin = previewRt.anchorMax = new Vector2(0.5f, 0.5f);
            previewRt.sizeDelta = rootRt != null ? rootRt.sizeDelta : Vector2.zero;
            previewRt.anchoredPosition = Vector2.zero;
            previewObj.transform.SetParent(rootNode);
            previewObj.transform.SetAsFirstSibling();

            var img = previewObj.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.color = Color.white;

            Debug.Log($"[蓝湖转换] 预览图已添加: {assetPath}", previewObj);
        }

        void OptimizeTransform(RectTransform trans)
        {
            var pos = trans.anchoredPosition3D;
            pos.x = Mathf.RoundToInt(pos.x);
            pos.y = Mathf.RoundToInt(pos.y);
            pos.z = Mathf.RoundToInt(pos.z);
            trans.anchoredPosition3D = pos;

            var size = trans.sizeDelta;
            size.x = Mathf.RoundToInt(size.x);
            size.y = Mathf.RoundToInt(size.y);
            trans.sizeDelta = size;

            var rot = trans.localEulerAngles;
            rot.x = Mathf.RoundToInt(rot.x);
            rot.y = Mathf.RoundToInt(rot.y);
            rot.z = Mathf.RoundToInt(rot.z);
            trans.localEulerAngles = rot;

            trans.localScale = Vector3.one;

            foreach (RectTransform item in trans)
                OptimizeTransform(item);
        }

        FontStyle GetFontStyle(string fontName)
        {
            if (fontName.Contains("Medium"))
                return FontStyle.Bold;
            return FontStyle.Normal;
        }

        static string FormatBeijingTime(string utcTimeStr)
        {
            if (string.IsNullOrEmpty(utcTimeStr)) return "-";
            if (DateTime.TryParse(utcTimeStr, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var utcTime))
            {
                var bjTime = utcTime.AddHours(8);
                return bjTime.ToString("yy/MM/dd HH:mm");
            }
            return utcTimeStr;
        }

        bool IsImageComponent(string name)
        {
            return Regex.IsMatch(name, @"^[a-zA-Z0-9_]+$");
        }

        void SortProjectImages()
        {
            Comparison<LanhuImageItem> comparison = sortColumn switch
            {
                1 => (a, b) => string.Compare(a.create_time, b.create_time, StringComparison.Ordinal),
                2 => (a, b) => string.Compare(a.update_time, b.update_time, StringComparison.Ordinal),
                _ => (a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal),
            };
            projectImages.Sort(comparison);
            if (!sortAscending) projectImages.Reverse();
        }

        void SavePublicModules()
        {
            EditorPrefs.SetString(PublicModulesPrefsKey, string.Join("|", publicModulePaths));
        }

        void SaveFontDirs()
        {
            EditorPrefs.SetString(FontDirsPrefsKey, string.Join("|", fontDirPaths));
        }

        void SaveExternalUIDirs()
        {
            EditorPrefs.SetString(ExternalUIDirPrefsKey, string.Join("|", externalUIDirPaths));
        }

        void ResetSelection()
        {
            selectedImageId = "";
            selectedImageName = "";
        }

        #endregion

        #region Lanhu API

        string HttpGet(string url)
        {
            using var www = UnityWebRequest.Get(url);
            if (!string.IsNullOrEmpty(lanhuCookie))
                www.SetRequestHeader("Cookie", lanhuCookie);
            www.SendWebRequest();
            while (!www.isDone) { }
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[蓝湖转换] 请求失败: {url}\n{www.error}");
                return null;
            }
            return www.downloadHandler.text;
        }

        string ParseProjectId(string url)
        {
            var match = Regex.Match(url, @"[?&](?:pid|project_id)=([^&#]+)");
            if (match.Success) return match.Groups[1].Value;
            return null;
        }

        string ParseImageId(string url)
        {
            var match = Regex.Match(url, @"[?&](?:image_id)=([^&#]+)");
            if (match.Success) return match.Groups[1].Value;
            return null;
        }

        void FetchProjectInfo()
        {
            projectId = ParseProjectId(jsonUrl);
            if (string.IsNullOrEmpty(projectId))
            {
                Debug.LogError("[蓝湖转换] 无法从URL中解析出 project_id");
                return;
            }

            var teamsJson = HttpGet("https://lanhuapp.com/api/account/user_teams");
            if (teamsJson == null) return;

            var teamsResp = JsonUtility.FromJson<LanhuApiTeamsResponse>(teamsJson);
            if (teamsResp?.result == null || teamsResp.result.Count == 0)
            {
                Debug.LogError("[蓝湖转换] 获取团队信息失败，请检查 Cookie 是否有效");
                return;
            }
            teamId = teamsResp.result[0].id;

            var infoUrl = $"https://lanhuapp.com/api/project/multi_info?project_id={projectId}&team_id={teamId}&img_limit=1&detach=1";
            var infoJson = HttpGet(infoUrl);
            if (infoJson == null) return;

            var infoResp = JsonUtility.FromJson<LanhuApiMultiInfoResponse>(infoJson);
            if (infoResp?.result?.images == null)
            {
                Debug.LogError("[蓝湖转换] 获取项目图片列表失败");
                return;
            }

            projectImages = infoResp.result.images;
            SortProjectImages();
            projectImages.ForEach(x => x.name = x.name.Replace("\n", string.Empty).Replace("\r", string.Empty));
            ResetSelection();

            imageId = ParseImageId(jsonUrl);
            if (!string.IsNullOrEmpty(imageId))
            {
                for (var i = 0; i < projectImages.Count; i++)
                {
                    if (projectImages[i].id == imageId)
                    {
                        selectedImageId = projectImages[i].id;
                        selectedImageName = projectImages[i].name;
                        imageListScrollPos.y = i * EditorGUIUtility.singleLineHeight;
                        break;
                    }
                }
            }

            Debug.Log($"[蓝湖转换] 获取成功，共 {projectImages.Count} 个界面");
        }

        string FetchImageJsonContent(string imgId)
        {
            var detailUrl = $"https://lanhuapp.com/api/project/image?dds_status=1&image_id={imgId}&team_id={teamId}&project_id={projectId}";
            var detailJson = HttpGet(detailUrl);
            if (detailJson == null) return null;

            var detailResp = JsonUtility.FromJson<LanhuApiImageResponse>(detailJson);
            var jsonDataUrl = detailResp?.result?.versions?[0]?.json_url;
            if (string.IsNullOrEmpty(jsonDataUrl))
            {
                Debug.LogError("[蓝湖转换] 未获取到 json_url");
                return null;
            }

            var content = HttpGet(jsonDataUrl);
            if (content == null) return null;

            var isV2 = jsonDataUrl.Contains("SketchJSONURL");
            Debug.Log($"[蓝湖转换] 数据版本: {(isV2 ? "v2" : "v1")}");

            return content;
        }

        #endregion
    }

    public enum ImportMode
    {
        JsonUrl,
        JsonFile
    }

    #region API Response Models

    [Serializable]
    public class LanhuApiTeamsResponse
    {
        public List<LanhuTeamInfo> result;
    }

    [Serializable]
    public class LanhuTeamInfo
    {
        public string id;
        public string name;
    }

    [Serializable]
    public class LanhuApiMultiInfoResponse
    {
        public LanhuMultiInfoResult result;
    }

    [Serializable]
    public class LanhuMultiInfoResult
    {
        public List<LanhuImageItem> images;
    }

    [Serializable]
    public class LanhuImageItem
    {
        public string id;
        public string name;
        public string url;
        public string create_time;
        public string update_time;
    }

    [Serializable]
    public class LanhuApiImageResponse
    {
        public LanhuImageDetailResult result;
    }

    [Serializable]
    public class LanhuImageDetailResult
    {
        public List<LanhuVersionInfo> versions;
    }

    [Serializable]
    public class LanhuVersionInfo
    {
        public string json_url;
    }

    #endregion
}

namespace Lanhu.DataV2
{
    [Serializable]
    public class LanhuSketchData
    {
        public bool flow;
        public string url_md5;
        public bool isMergeData;
        public string md5;
        public string json_md5;
        public string type;
        public string ArtboardID;
        public string plVersion;
        public object sketchVersion;
        public string device;
        public float ArtboardScale;
        public List<LanhuLayerInfo> info;
    }

    [Serializable]
    public class LanhuLayerInfo
    {
        public string id;
        public string name;
        public string parentID;
        public float left;
        public float top;
        public float width;
        public float height;
        public string type;
        public bool isVisible;
        public List<LanhuLayerInfo> layers;
        public bool isFlippedHorizontal;
        public bool isFlippedVertical;
        public int blendMode;
        public float opacity;
        public float rotation;
        public bool exportable;
        public bool hasExportDDSImage;
        public LanhuImageInfo image;
        public LanhuFontInfo font;
        public List<LanhuFillStyle> fills;
        [NonSerialized] public bool isRoot;
        [NonSerialized] public List<LanhuLayerInfo> children = new();
    }

    [Serializable]
    public class LanhuFrame
    {
        public float x;
        public float y;
        public float width;
        public float height;
    }

    [Serializable]
    public class LanhuImageInfo
    {
        public int isNew;
        public LanhuSize size;
        public string imageUrl;
    }

    [Serializable]
    public class LanhuSize
    {
        public float width;
        public float height;
    }

    [Serializable]
    public class LanhuFontInfo
    {
        public float size;
        public string displayName;
        public string content;
        public List<LanhuTextStyle> styles;
    }

    [Serializable]
    public class LanhuTextStyle
    {
        public LanhuColorInfo color;
        public string content;
        public string font;
        public int alignment;
    }

    [Serializable]
    public class LanhuFillStyle
    {
        public LanhuColorInfo color;
        public LanhuGradientInfo gradient;
        public string type;
        public int fillType;
        public float opacity;
        public bool isEnabled;
    }

    [Serializable]
    public class LanhuGradientInfo
    {
        public List<LanhuGradientColorStop> colorStops;
        public string type;
        public int gradientType;
        public LanhuGradientPoint from;
        public LanhuGradientPoint to;
        public float elipseLength;
    }

    [Serializable]
    public class LanhuGradientColorStop
    {
        public float position;
        public LanhuColorInfo color;
    }

    [Serializable]
    public class LanhuGradientPoint
    {
        public float x;
        public float y;
    }

    [Serializable]
    public class LanhuColorInfo
    {
        public string value;
        public float r;
        public float g;
        public float b;
        public float a;

        public static implicit operator Color(LanhuColorInfo info)
        {
            return ParseRgba(info.value);
        }

        static Color ParseRgba(string rgbaString)
        {
            if (string.IsNullOrEmpty(rgbaString)) return Color.white;

            var match = Regex.Match(rgbaString, @"rgba\((\d+),(\d+),(\d+),([\d\.]+)\)");
            if (match.Success)
            {
                var r = float.Parse(match.Groups[1].Value) / 255f;
                var g = float.Parse(match.Groups[2].Value) / 255f;
                var b = float.Parse(match.Groups[3].Value) / 255f;
                var a = float.Parse(match.Groups[4].Value);
                return new Color(r, g, b, a);
            }
            return Color.white;
        }
    }
}

namespace Lanhu.DataV1
{
    [Serializable]
    public class LanhuBoardData
    {
        public LanhuBoard board;
    }

    [Serializable]
    public class LanhuBoard
    {
        public int id;
        public int index;
        public string type;
        public string name;
        public bool visible;
        public bool clipped;
        public bool generatorSettings;
        public LanhuArtboard artboard;
        public List<LanhuLayer> layers;
    }

    [Serializable]
    public class LanhuArtboard
    {
        public LanhuRect artboardRect;
    }

    [Serializable]
    public class LanhuRect
    {
        public float top;
        public float left;
        public float bottom;
        public float right;
    }

    [Serializable]
    public class LanhuLayer
    {
        public int id;
        public string type;
        public string name;
        public bool visible;
        public bool clipped;
        public bool generatorSettings;
        public float width;
        public float height;
        public float top;
        public float left;
        public bool text;
        public LanhuTextInfo textInfo;
        public LanhuSmartObject smartObject;
        public bool isAsset;
        public bool isSlice;
        public LanhuRect _orgBounds;
        public LanhuRealFrame realFrame;
        public LanhuDdsImages ddsImages;
        public bool hasExportDDSImage;
        public List<LanhuLayer> layers;
        public List<DataV2.LanhuFillStyle> fills;
    }

    [Serializable]
    public class LanhuTextInfo
    {
        public string text;
        public LanhuColorRGB color;
        public float size;
        public string fontPostScriptName;
        public bool bold;
        public bool italic;
        public string justification;
        public float leading;
        public List<LanhuTextStyleRange> textStyleRange;
        public string fontName;
        public string fontStyleName;
        public string antiAlias;
        public LanhuRect bounds;
        public LanhuRect boundingBox;
    }

    [Serializable]
    public class LanhuColorRGB
    {
        public int r;
        public int g;
        public int b;

        public static implicit operator Color(LanhuColorRGB c)
        {
            if (c == null) return Color.white;
            return new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
        }
    }

    [Serializable]
    public class LanhuTextStyleRange
    {
        public int from;
        public int to;
        public LanhuTextStyle textStyle;
    }

    [Serializable]
    public class LanhuTextStyle
    {
        public string fontName;
        public string fontStyleName;
        public float size;
        public string fontPostScriptName;
        public bool autoLeading;
        public float impliedFontSize;
        public float impliedLeading;
        public float impliedBaselineShift;
        public bool contextualLigatures;
        public LanhuColorNamed color;
        public float leading;
    }

    [Serializable]
    public class LanhuColorNamed
    {
        public int red;
        public int green;
        public int blue;

        public static implicit operator Color(LanhuColorNamed c)
        {
            if (c == null) return Color.white;
            return new Color(c.red / 255f, c.green / 255f, c.blue / 255f, 1f);
        }
    }

    [Serializable]
    public class LanhuSmartObject
    {
        public string ID;
        public string placed;
        public int crop;
        public int antiAliasType;
        public int type;
        public List<float> transform;
        public int comp;
    }

    [Serializable]
    public class LanhuRealFrame
    {
        public float left;
        public float top;
        public float width;
        public float height;
    }

    [Serializable]
    public class LanhuDdsImages
    {
        public string orgUrl;
        public string @base;
    }
}
