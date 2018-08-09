﻿// TODO
// Scale thumbnail by shorter axis
// Page 0 for quick save, -1 for auto save, last page for new page
// UI to edit bookmark's description
// Compress thumbnail
//
// Function to update bookmark's description

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Nova
{
    public enum SaveViewMode
    {
        Save,
        Load
    }

    public class BookmarkSaveEventData
    {
        public BookmarkSaveEventData(int saveId, Bookmark bookmark)
        {
            this.saveId = saveId;
            this.bookmark = bookmark;
        }

        public int saveId { get; private set; }
        public Bookmark bookmark { get; private set; }
    }

    [System.Serializable]
    public class BookmarkSaveEvent : UnityEvent<BookmarkSaveEventData>
    {
    }

    public class BookmarkLoadEventData
    {
        public BookmarkLoadEventData(Bookmark bookmark)
        {
            this.bookmark = bookmark;
        }

        public Bookmark bookmark { get; private set; }
    }

    [System.Serializable]
    public class BookmarkLoadEvent : UnityEvent<BookmarkLoadEventData>
    {
    }

    public class BookmarkDeleteEventData
    {
        public BookmarkDeleteEventData(int saveId)
        {
            this.saveId = saveId;
        }

        public int saveId { get; private set; }
    }

    [System.Serializable]
    public class BookmarkDeleteEvent : UnityEvent<BookmarkDeleteEventData>
    {
    }

    public class SaveViewController : MonoBehaviour
    {
        public GameObject SaveEntryPrefab;
        public GameObject SaveEntryRowPrefab;
        public int maxRow;
        public int maxCol;
        public bool canSave;

        public BookmarkSaveEvent BookmarkSave;
        public BookmarkLoadEvent BookmarkLoad;
        public BookmarkDeleteEvent BookmarkDelete;

        private const string saveBookmarkComfirmText = "覆盖存档{0}？";
        private const string loadBookmarkComfirmText = "读取存档{0}？";
        private const string deleteBookmarkComfirmText = "删除存档{0}？";

        private GameState gameState;
        private CheckpointManager checkpointManager;

        private int maxSaveEntry;
        private int page = 1;

        // maxPage is updated when ShowPage is called
        private int maxPage = 1;

        // selectedSaveId == -1 means no bookmark is selected
        private int _selectedSaveId;

        private int selectedSaveId
        {
            get { return _selectedSaveId; }

            set
            {
                Assert.IsTrue(usedSaveSlots.ContainsKey(value) || value == -1,
                    "Nova: selectedSaveId must be a saveId with existing bookmark, or -1");
                _selectedSaveId = value;
                if (value == -1)
                {
                    ShowPreviewScreen();
                }
                else
                {
                    ShowPreviewBookmark(value);
                }
            }
        }

        private GameObject savePanel;
        private Button backgroundButton;
        private Image thumbnailImage;
        private Text thumbnailText;
        private Button saveButton;
        private Button loadButton;
        private Button leftButton;
        private Button rightButton;
        private Text leftButtonText;
        private Text rightButtonText;
        private Text pageText;
        private Sprite defaultThumbnailSprite;

        private readonly List<GameObject> saveEntries = new List<GameObject>();
        private Dictionary<int, BookmarkMetadata> usedSaveSlots;
        private readonly Dictionary<int, Sprite> _cachedThumbnailSprite = new Dictionary<int, Sprite>();

        private SaveViewMode saveViewMode;

        private ScreenCapturer screenCapturer;

        // screenTexture and screenSprite are created when Show is called and savePanel is not active
        // They are destroyed when Hide is called and savePanel is active
        private Texture2D screenTexture;
        private Sprite screenSprite;

        private const string dateTimeFormat = "yyyy/MM/dd HH:mm";
        private string previewTextFormat;
        private string currentNodeName;
        private string currentDialogueText;

        private AlertController alertController;

        private void Awake()
        {
            maxSaveEntry = maxRow * maxCol;

            gameState = Utils.FindNovaGameController().GetComponent<GameState>();
            checkpointManager = Utils.FindNovaGameController().GetComponent<CheckpointManager>();

            savePanel = transform.Find("SavePanel").gameObject;
            backgroundButton = savePanel.transform.Find("Background").GetComponent<Button>();
            thumbnailImage = savePanel.transform.Find("Background/Left/Thumbnail").GetComponent<Image>();
            defaultThumbnailSprite = thumbnailImage.sprite;
            thumbnailText = savePanel.transform.Find("Background/Left/TextBox/Text").GetComponent<Text>();
            var headerPanel = savePanel.transform.Find("Background/Right/Bottom").gameObject;
            saveButton = headerPanel.transform.Find("SaveButton").GetComponent<Button>();
            loadButton = headerPanel.transform.Find("LoadButton").GetComponent<Button>();
            var pagerPanel = headerPanel.transform.Find("Pager").gameObject;
            var leftButtonPanel = pagerPanel.transform.Find("LeftButton").gameObject;
            leftButton = leftButtonPanel.GetComponent<Button>();
            leftButtonText = leftButtonPanel.GetComponent<Text>();
            var rightButtonPanel = pagerPanel.transform.Find("RightButton").gameObject;
            rightButton = rightButtonPanel.GetComponent<Button>();
            rightButtonText = rightButtonPanel.GetComponent<Text>();
            pageText = pagerPanel.transform.Find("PageText").GetComponent<Text>();

            backgroundButton.onClick.AddListener(() => { selectedSaveId = -1; });
            if (canSave)
            {
                saveButton.onClick.AddListener(() => ShowSave());
            }
            else
            {
                // Cannot SetActive(false), otherwise layout will break
                saveButton.GetComponent<CanvasGroup>().alpha = 0.0f;
            }

            loadButton.onClick.AddListener(() => ShowLoad());
            leftButton.onClick.AddListener(() => PageLeft());
            rightButton.onClick.AddListener(() => PageRight());

            var saveEntryGrid = savePanel.transform.Find("Background/Right/Top").gameObject;
            for (var rowIdx = 0; rowIdx < maxRow; ++rowIdx)
            {
                var saveEntryRow = Instantiate(SaveEntryRowPrefab);
                saveEntryRow.transform.SetParent(saveEntryGrid.transform);
                saveEntryRow.transform.localScale = Vector3.one;
                for (var colIdx = 0; colIdx < maxCol; ++colIdx)
                {
                    var saveEntry = Instantiate(SaveEntryPrefab);
                    saveEntry.transform.SetParent(saveEntryRow.transform);
                    saveEntry.transform.localScale = Vector3.one;
                    saveEntries.Add(saveEntry);
                }
            }

            screenCapturer = gameObject.GetComponent<ScreenCapturer>();

            previewTextFormat = thumbnailText.text;

            alertController = GameObject.FindWithTag("Alert").GetComponent<AlertController>();
            
            gameState.DialogueChanged += OnDialogueChanged;
        }

        private void Start()
        {
            usedSaveSlots = checkpointManager.SaveSlotsMetadata;
            ShowPage();
        }

        private void OnDestroy()
        {
            gameState.DialogueChanged -= OnDialogueChanged;
        }

        private void OnDialogueChanged(DialogueChangedData dialogueChangedData)
        {
            currentNodeName = dialogueChangedData.nodeName;
            currentDialogueText = dialogueChangedData.text;
        }

        private void Show()
        {
            if (!savePanel.activeSelf)
            {
                screenTexture = screenCapturer.GetTexture();
                screenSprite = Utils.Texture2DToSprite(screenTexture);
            }

            savePanel.SetActive(true);
            selectedSaveId = -1;
            ShowPage();
        }

        public void ShowSave()
        {
            saveViewMode = SaveViewMode.Save;
            Show();
        }

        public void ShowLoad()
        {
            saveViewMode = SaveViewMode.Load;
            Show();
        }

        public void Hide()
        {
            if (savePanel.activeSelf)
            {
                Destroy(screenTexture);
                Destroy(screenSprite);
                screenTexture = null;
                screenSprite = null;
            }

            savePanel.SetActive(false);
        }

        private void PageLeft()
        {
            if (page > 1)
            {
                --page;
                ShowPage();
            }
        }

        private void PageRight()
        {
            if (page < maxPage)
            {
                ++page;
                ShowPage();
            }
        }

        private void _saveBookmark(int saveId)
        {
            var bookmark = gameState.GetBookmark();
            bookmark.ScreenShot = screenSprite.texture;
            DeleteCachedThumbnailSprite(saveId);
            BookmarkSave.Invoke(new BookmarkSaveEventData(saveId, bookmark));
        }

        private void SaveBookmark(int saveId)
        {
            alertController.Alert(
                null,
                string.Format(saveBookmarkComfirmText, saveId),
                () => _saveBookmark(saveId)
            );
        }

        private void _loadBookmark(int saveId)
        {
            var bookmark = checkpointManager.LoadBookmark(saveId);
            BookmarkLoad.Invoke(new BookmarkLoadEventData(bookmark));
        }

        private void LoadBookmark(int saveId)
        {
            alertController.Alert(
                null,
                string.Format(loadBookmarkComfirmText, saveId),
                () => _loadBookmark(saveId)
            );
        }

        private void _deleteBookmark(int saveId)
        {
            DeleteCachedThumbnailSprite(saveId);
            BookmarkDelete.Invoke(new BookmarkDeleteEventData(saveId));
        }

        private void DeleteBookmark(int saveId)
        {
            alertController.Alert(
                null,
                string.Format(deleteBookmarkComfirmText, saveId),
                () => _deleteBookmark(saveId)
            );
        }

        private void OnThumbnailButtonClicked(int saveId)
        {
            if (Input.touchCount == 0) // Mouse
            {
                if (saveViewMode == SaveViewMode.Save)
                {
                    if (usedSaveSlots.ContainsKey(saveId))
                    {
                        SaveBookmark(saveId);
                    }
                    else // Bookmark with this saveId does not exist
                    {
                        // No alert when saving to an empty slot
                        _saveBookmark(saveId);
                    }
                }
                else // saveViewMode == SaveViewMode.Load
                {
                    if (usedSaveSlots.ContainsKey(saveId))
                    {
                        LoadBookmark(saveId);
                    }
                }
            }
            else // Touch
            {
                if (saveViewMode == SaveViewMode.Save)
                {
                    if (saveId == selectedSaveId)
                    {
                        SaveBookmark(saveId);
                    }
                    else // Another bookmark selected
                    {
                        if (usedSaveSlots.ContainsKey(saveId))
                        {
                            selectedSaveId = saveId;
                        }
                        else // Bookmark with this saveId does not exist
                        {
                            selectedSaveId = -1;
                            // No alert when saving to an empty slot
                            _saveBookmark(saveId);
                        }
                    }
                }
                else // saveViewMode == SaveViewMode.Load
                {
                    if (saveId == selectedSaveId)
                    {
                        LoadBookmark(saveId);
                    }
                    else // Another bookmark selected
                    {
                        if (usedSaveSlots.ContainsKey(saveId))
                        {
                            selectedSaveId = saveId;
                        }
                        else // Bookmark with this saveId does not exist
                        {
                            selectedSaveId = -1;
                        }
                    }
                }
            }
        }

        private void OnThumbnailButtonEnter(int saveId)
        {
            if (Input.touchCount == 0) // Mouse
            {
                if (usedSaveSlots.ContainsKey(saveId))
                {
                    selectedSaveId = saveId;
                }
            }
        }

        private void OnThumbnailButtonExit(int saveId)
        {
            if (Input.touchCount == 0) // Mouse
            {
                selectedSaveId = -1;
            }
        }

        private void ShowPreview(Sprite newThumbnailSprite, string newText)
        {
            if (newThumbnailSprite == null)
            {
                thumbnailImage.sprite = defaultThumbnailSprite;
            }
            else
            {
                thumbnailImage.sprite = newThumbnailSprite;
            }

            thumbnailText.text = newText;
        }

        private void ShowPreviewScreen()
        {
            ShowPreview(screenSprite, string.Format(
                previewTextFormat,
                DateTime.Now.ToString(dateTimeFormat),
                currentNodeName,
                currentDialogueText
            ));
        }

        private void ShowPreviewBookmark(int saveId)
        {
            Bookmark bookmark = checkpointManager[saveId];
            ShowPreview(GetThumbnailSprite(saveId), string.Format(
                previewTextFormat,
                usedSaveSlots[saveId].ModifiedTime.ToString(dateTimeFormat),
                bookmark.NodeHistory.Last(),
                bookmark.Description
            ));
        }

        public void ShowPage()
        {
            if (usedSaveSlots.Any())
            {
                maxPage = (usedSaveSlots.Keys.Max() + maxSaveEntry - 1) / maxSaveEntry;
                // New page to save
                if (saveViewMode == SaveViewMode.Save)
                {
                    ++maxPage;
                }
            }
            else
            {
                maxPage = 1;
            }

            if (maxPage < page)
            {
                page = maxPage;
            }

            pageText.text = string.Format("{0} / {1}", page, maxPage);

            if (saveViewMode == SaveViewMode.Save)
            {
                saveButton.interactable = false;
                loadButton.interactable = true;
            }
            else // saveViewMode == SaveViewMode.Load
            {
                saveButton.interactable = true;
                loadButton.interactable = false;
            }

            for (var i = 0; i < maxSaveEntry; ++i)
            {
                int saveId = (page - 1) * maxSaveEntry + i + 1;
                string newIdText = saveId.ToString();

                // Load properties from bookmark
                string newHeaderText;
                string newFooterText;
                Sprite newThumbnailSprite;
                UnityAction onEditButtonClicked;
                UnityAction onDeleteButtonClicked;
                if (usedSaveSlots.ContainsKey(saveId))
                {
                    Bookmark bookmark = checkpointManager[saveId];
                    newHeaderText = bookmark.NodeHistory.Last();
                    newFooterText = bookmark.CreationTime.ToString(dateTimeFormat);
                    newThumbnailSprite = GetThumbnailSprite(saveId);
                    onEditButtonClicked = null;
                    onDeleteButtonClicked = () => DeleteBookmark(saveId);
                }
                else
                {
                    newHeaderText = "";
                    newFooterText = "";
                    newThumbnailSprite = null;
                    onEditButtonClicked = null;
                    onDeleteButtonClicked = null;
                }

                UnityAction onThumbnailButtonClicked = () => OnThumbnailButtonClicked(saveId);
                UnityAction onThumbnailButtonEnter = () => OnThumbnailButtonEnter(saveId);
                UnityAction onThumbnailButtonExit = () => OnThumbnailButtonExit(saveId);

                // Update UI of saveEntry
                var saveEntry = saveEntries[i];
                var saveEntryController = saveEntry.GetComponent<SaveEntryController>();
                saveEntryController.Init(newIdText, newHeaderText, newFooterText, newThumbnailSprite,
                    onEditButtonClicked, onDeleteButtonClicked,
                    onThumbnailButtonClicked, onThumbnailButtonEnter, onThumbnailButtonExit);
            }
        }

        private Sprite GetThumbnailSprite(int saveId)
        {
            Assert.IsTrue(usedSaveSlots.ContainsKey(saveId),
                "Nova: GetThumbnailSprite must use a saveId with existing bookmark");
            if (!_cachedThumbnailSprite.ContainsKey(saveId))
            {
                Bookmark bookmark = checkpointManager[saveId];
                _cachedThumbnailSprite[saveId] = Utils.Texture2DToSprite(bookmark.ScreenShot);
            }

            return _cachedThumbnailSprite[saveId];
        }

        private void DeleteCachedThumbnailSprite(int saveId)
        {
            if (_cachedThumbnailSprite.ContainsKey(saveId))
            {
                Destroy(_cachedThumbnailSprite[saveId]);
                _cachedThumbnailSprite.Remove(saveId);
            }
        }
    }
}