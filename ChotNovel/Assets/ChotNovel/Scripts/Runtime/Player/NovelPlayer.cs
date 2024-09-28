using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ChotNovel.Player
{
    public class NovelPlayer : MonoBehaviour
    {
        private ITextContainer _textContainer;
        private PlaybackParameters _playbackParams = new PlaybackParameters();
        private string _lastPlayedFileName;
        private List<TextElement> _textElementBuffer = new List<TextElement>();
        private CancellationTokenSource _playerCancellation;

        private void Awake()
        {
            _textContainer = GetComponent<ITextContainer>();
            if (_textContainer == null)
            {
                Debug.LogError("ITextContainer is not found.");
                enabled = false;
            }

            _playbackParams.Payload.Player = this;
        }

        /// <summary>
        /// Start playback from a specific label within a specific file.
        /// </summary>
        public void Play(string file, string label, int step)
        {
            if (string.IsNullOrEmpty(file))
            {
                Debug.LogError("File name is empty.");
                return;
            }

            if (string.IsNullOrEmpty(label))
            {
                Debug.LogError("Label is empty.");
                return;
            }

            if (_playerCancellation != null)
            {
                _playerCancellation.Cancel();
                _playerCancellation.Dispose();
                _playerCancellation = null;
            }
            _playerCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            PlayInternal(file, label, step, _playerCancellation.Token).Forget();
        }

        private async UniTask PlayInternal(string file, string label, int step, CancellationToken cancellationToken)
        {
            ClearDisplayedObjects();

            var analyzer = new NovelPlaybackPositionAnalyzer();
            analyzer.AddJumpCommand("jump");
            analyzer.AddJumpCommand("choice");
            _textElementBuffer.Clear();
            var result = await analyzer.CollectPreviousTextElements(file, label, step, _textContainer, _textElementBuffer, cancellationToken);
            if (!result)
            {
                Debug.LogError($"Failed to analyze playback position. file:{file}, label:{label}, step:{step}");
                return;
            }

            var playbackController = new DefaultPlaybackController();
            // Play previous text elements. (fast speed)
            if (_textElementBuffer.Count > 0)
            {
                _playbackParams.Payload.IgnoreWait = true;
                await playbackController.PlayTexts(_textElementBuffer, _playbackParams, cancellationToken);
            }
            // Play text elements. (default speed)
            _textElementBuffer.Clear();
            if (await LoadLabeledTextElements(file, label, _textElementBuffer, cancellationToken))
            {
                _lastPlayedFileName = file;
                _playbackParams.Payload.IgnoreWait = false;
                _textElementBuffer.RemoveRange(0, step);
                await playbackController.PlayTexts(_textElementBuffer, _playbackParams, cancellationToken);
            }
        }

        /// <summary>
        /// Jump to a specific label within the file being played.
        /// </summary>
        public void Jump(string label)
        {
            Jump(_lastPlayedFileName, label);
        }

        /// <summary>
        /// Jump to a specific label within a specific file.
        /// </summary>
        public void Jump(string file, string label)
        {
            if (string.IsNullOrEmpty(file))
            {
                Debug.LogError("File name is empty.");
                return;
            }

            if (string.IsNullOrEmpty(label))
            {
                Debug.LogError("Label is empty.");
                return;
            }

            if (_playerCancellation != null)
            {
                _playerCancellation.Cancel();
                _playerCancellation.Dispose();
                _playerCancellation = null;
            }
            _playerCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            JumpInternal(file, label, _playerCancellation.Token).Forget();
        }

        private async UniTask JumpInternal(string file, string label, CancellationToken cancellationToken)
        {
            var labeledTextElements = new List<TextElement>();
            if (await LoadLabeledTextElements(file, label, labeledTextElements, cancellationToken))
            {
                _lastPlayedFileName = file;
                var playbackController = new DefaultPlaybackController();
                await playbackController.PlayTexts(labeledTextElements, _playbackParams, cancellationToken);
            }
        }

        private readonly List<TextElement> _fileElementBuffer = new List<TextElement>();
        private async UniTask<bool> LoadLabeledTextElements(string file, string label, List<TextElement> results, CancellationToken cancellationToken)
        {
            _fileElementBuffer.Clear();
            results.Clear();
            var success = await _textContainer.LoadTextElements(file, _fileElementBuffer, cancellationToken)
                && NovelPlayerUtility.PickLabeledTextElements(_fileElementBuffer, label, results);
            if (!success)
            {
                Debug.LogError($"Failed to get text elements. file:{file}, label:{label}");
                return false;
            }
            return true;
        }

        public void RegisterModule(NovelModule module)
        {
            _playbackParams.Modules.Add(module);
        }

        public void UnregisterModule(NovelModule module)
        {
            _playbackParams.Modules.Remove(module);
        }

        public void ClearDisplayedObjects()
        {
            foreach (var module in _playbackParams.Modules)
            {
                module.ClearDisplayedObjects();
            }
        }

        public class PlaybackParameters
        {
            public NovelModulePayload Payload = new NovelModulePayload();
            public List<NovelModule> Modules = new List<NovelModule>();
        }

        private class DefaultPlaybackController
        {
            public async UniTask PlayTexts(IReadOnlyList<TextElement> textElements, PlaybackParameters playbackParams, CancellationToken cancellationToken)
            {
                foreach (var textElement in textElements)
                {
                    await PlayModules(textElement, playbackParams.Payload, playbackParams.Modules, cancellationToken);
                }
            }

            private async UniTask PlayModules(TextElement textElement, NovelModulePayload payload, IReadOnlyList<NovelModule> modules, CancellationToken cancellationToken)
            {
                var excuted = false;
                foreach (var module in modules)
                {
                    if (module.IsExecutable(textElement))
                    {
                        excuted = true;
                        await module.Execute(textElement, payload, cancellationToken);
                    }
                }

                if (!excuted && textElement.ElementType != TextElementType.Label)
                {
                    Debug.LogWarning($"No module is executed for {textElement.Content}");
                }
            }
        }
    }
}
