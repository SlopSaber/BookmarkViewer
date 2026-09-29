using System;
using System.ComponentModel;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.Settings;
using UnityEngine;
using Zenject;

namespace BookmarkViewer.UI
{
	public class BookmarkSettingsMenu : MonoBehaviour, IInitializable, INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		private Config Settings => Config.Instance ?? throw new InvalidOperationException("BookmarkViewer settings are not initialized.");


        [UIValue("enabled")]
        public bool Enabled
        {
            get => Settings.Enabled;
            set
            {
                Settings.Enabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            }
        }

        [UIValue("snap")]
        public bool SnapToBookmark
        {
            get => Settings.SnapToBookmark;
            set
            {
                Settings.SnapToBookmark = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapToBookmark)));
            }

        }
        [UIValue("skew")]
        public bool UnskewBookmarks
        {
            get => Settings.UnskewBookmarks;
            set
            {
                Settings.UnskewBookmarks = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnskewBookmarks)));
            }
        }

        [UIValue("width")]
        public float BookmarkWidthSize
        {
            get => Settings.BookmarkWidthSize;
            set
            {
                Settings.BookmarkWidthSize = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BookmarkWidthSize)));
            }
        }

        public void Initialize()
		{
            BSMLSettings.Instance.AddSettingsMenu("BookmarkViewer", "BookmarkViewer.UI.Menu.bsml", this);
		}
	}
}
