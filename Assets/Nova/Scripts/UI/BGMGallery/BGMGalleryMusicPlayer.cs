using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace Nova
{
    /// use BGMGalleryMusicPlayer.Play and BGMGalleryMusicPlayer.Pause instead of manipulate
    /// the underlying AudioSource directly
    /// BGMGalleryMusicPlayer will maintain a IsPlaying status, it will be sync with AudioSource.isPlaying
    /// If IsPlaying flag is out of sync, the underlying clip has finished playing. The player will play the
    /// next music in its playing list
    public class BGMGalleryMusicPlayer : MonoBehaviour
    {
        public AudioSource audioSource;
        public Text titleLabel;
        public BGMGalleryMusicProgressBar progressBar;

        public bool isPlaying { get; private set; }

        private void ApplyInvalidMusicEntry()
        {
            audioSource.clip = null;
            titleLabel.text = I18n.__("bgmgallery.title");
            progressBar.interactable = false;
        }

        private void ApplyMusicEntry(MusicEntry music)
        {
            Assert.IsNotNull(music);
            audioSource.clip = AssetLoader.Load<AudioClip>(music.resourcePath);
            titleLabel.text = music.GetDisplayName();
            progressBar.interactable = true;
        }

        private void Start()
        {
            // it should wait for other components to be initialized
            Pause(); // sync IsPlaying flag on initialization
            ApplyInvalidMusicEntry();
        }

        private bool needResetMusicOffset = true;

        private MusicEntry _currentMusic;

        private MusicEntry currentMusic
        {
            set
            {
                if (_currentMusic == value)
                    return;
                _currentMusic = value;
                needResetMusicOffset = true;
                Pause();
                if (value == null)
                {
                    ApplyInvalidMusicEntry();
                }
                else
                {
                    ApplyMusicEntry(value);
                }
            }
        }

        private IMusicList _musicList;

        public IMusicList musicList
        {
            get => _musicList;
            set
            {
                _musicList = value;
                currentMusic = _musicList?.Current()?.entry;
            }
        }

        public void Play()
        {
            if (audioSource.isPlaying) return;
            if (needResetMusicOffset)
            {
                audioSource.time = 0;
                needResetMusicOffset = false;
            }

            isPlaying = true;
            audioSource.Play();
        }

        public void Pause()
        {
            isPlaying = false;
            audioSource.Pause();
        }

        public void Next()
        {
            if (musicList == null) return;
            Pause();
            needResetMusicOffset = true;
            currentMusic = musicList.Next().entry;
            Play();
        }

        private void Step()
        {
            if (musicList == null) return;
            Pause();
            needResetMusicOffset = true;
            currentMusic = musicList.Step().entry;
            Play();
        }

        public void Previous()
        {
            if (musicList == null) return;
            Pause();
            needResetMusicOffset = true;
            currentMusic = musicList.Previous().entry;
            Play();
        }

        private void Update()
        {
            if (audioSource.isPlaying == isPlaying) return;
            // out of sync with the underlying AudioSource
            // play the next song in the play list
            Assert.IsTrue(isPlaying);
            // out of sync also happens when the application lost focus
            // check the time to ensure the clip has finished playing
            if (Math.Abs(audioSource.time) < float.Epsilon ||
                Math.Abs(audioSource.time - audioSource.clip.length) < float.Epsilon)
            {
                Step();
            }
        }
    }
}