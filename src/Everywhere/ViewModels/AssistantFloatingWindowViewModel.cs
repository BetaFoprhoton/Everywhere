﻿using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Everywhere.Models;
using Everywhere.Utils;
using Lucide.Avalonia;
using Microsoft.Extensions.Logging;
using ObservableCollections;
using ZLinq;
using ChatMessage = Everywhere.Models.ChatMessage;

namespace Everywhere.ViewModels;

public partial class AssistantFloatingWindowViewModel : BusyViewModelBase
{
    public Settings Settings { get; }

    [ObservableProperty]
    public partial bool IsOpened { get; set; }

    [ObservableProperty]
    public partial PixelRect TargetBoundingRect { get; private set; }

    [ObservableProperty]
    public partial DynamicResourceKey? Title { get; private set; }

    [field: AllowNull, MaybeNull]
    public NotifyCollectionChangedSynchronizedViewList<ChatAttachment> ChatAttachments =>
        field ??= chatAttachments.ToNotifyCollectionChangedSlim(SynchronizationContextCollectionEventDispatcher.Current);

    [ObservableProperty]
    public partial IReadOnlyList<DynamicNamedCommand>? QuickActions { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ChatCommand>? ChatCommands { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public IChatContextManager ChatContextManager { get; }

    public IChatService ChatService { get; }

    private readonly IVisualElementContext visualElementContext;
    private readonly IClipboard clipboard;
    private readonly IStorageProvider storageProvider;
    private readonly INativeHelper nativeHelper;
    private readonly ILogger<AssistantFloatingWindowViewModel> logger;

    private readonly ObservableList<ChatAttachment> chatAttachments = [];
    private readonly ReusableCancellationTokenSource cancellationTokenSource = new();

    public AssistantFloatingWindowViewModel(
        IChatContextManager chatContextManager,
        IChatService chatService,
        Settings settings,
        IVisualElementContext visualElementContext,
        IClipboard clipboard,
        IStorageProvider storageProvider,
        INativeHelper nativeHelper,
        ILogger<AssistantFloatingWindowViewModel> logger)
    {
        ChatContextManager = chatContextManager;
        ChatService = chatService;
        Settings = settings;

        this.visualElementContext = visualElementContext;
        this.clipboard = clipboard;
        this.storageProvider = storageProvider;
        this.nativeHelper = nativeHelper;
        this.logger = logger;

        InitializeCommands();
    }

    private void InitializeCommands()
    {
        DynamicNamedCommand[] textEditActions =
        [
            new(
                LucideIconKind.Languages,
                LocaleKey.AssistantFloatingWindowViewModel_TextEditActions_Translate,
                null,
                SendMessageCommand,
                $"Translate the content in focused element to {new CultureInfo(Settings.Common.Language).Name}. " +
                $"If it's already in target language, translate it to English. " +
                $"You MUST only reply with the translated content, without any other text or explanation"
            ),
            new(
                LucideIconKind.StepForward,
                LocaleKey.AssistantFloatingWindowViewModel_TextEditActions_ContinueWriting,
                null,
                SendMessageCommand,
                "I have already written a beginning as the content of the focused element. " +
                "You MUST imitate my writing style and tone, then continue writing in my perspective. " +
                "You MUST only reply with the continue written content, without any other text or explanation"
            ),
            new(
                LucideIconKind.ScrollText,
                LocaleKey.AssistantFloatingWindowViewModel_TextEditActions_Summarize,
                null,
                SendMessageCommand,
                "Please summarize the content in focused element. " +
                "You MUST only reply with the summarize content, without any other text or explanation"
            )
        ];

        ChatCommand[] textEditCommands =
        [
            new(
                "/translate",
                LocaleKey.AssistantCommand_Translate_Description,
                "Based on context, translate the content of focused element into {0}",
                () => Settings.Common.Language),
            new(
                "/rewrite",
                LocaleKey.AssistantCommand_Rewrite_Description,
                "Based on context, rewrite the content of focused element"),
        ];

        void HandleChatAttachmentsCollectionChanged(in NotifyCollectionChangedEventArgs<ChatAttachment> x)
        {
            QuickActions = null;
            ChatCommands = null;

            if (chatAttachments.Count <= 0) return;

            switch (chatAttachments[0])
            {
                case ChatVisualElementAttachment { Element.Type: VisualElementType.TextEdit }:
                {
                    QuickActions = textEditActions;
                    ChatCommands = textEditCommands;
                    break;
                }
            }
        }

        chatAttachments.CollectionChanged += HandleChatAttachmentsCollectionChanged;
    }

    private CancellationTokenSource? targetElementChangedTokenSource;

    public async Task TryFloatToTargetElementAsync(IVisualElement? targetElement, bool showExpanded = false)
    {
        // debouncing
        if (targetElementChangedTokenSource is not null) await targetElementChangedTokenSource.CancelAsync();
        targetElementChangedTokenSource = new CancellationTokenSource();
        var cancellationToken = targetElementChangedTokenSource.Token;
        try
        {
            await Task.Delay(100, cancellationToken);
        }
        catch (OperationCanceledException) { }

        await ExecuteBusyTaskAsync(
            async token =>
            {
                if (chatAttachments.Any(a => a is ChatVisualElementAttachment vea && vea.Element.Equals(targetElement)))
                {
                    IsOpened = true;
                    return;
                }

                Reset();

                if (targetElement == null)
                {
                    return;
                }

                TargetBoundingRect = targetElement.BoundingRectangle;
                Title = LocaleKey.AssistantFloatingWindow_Title;
                chatAttachments.Clear();
                chatAttachments.Add(await Task.Run(() => CreateFromVisualElement(targetElement), token));
                IsOpened = true;
                IsExpanded = showExpanded;
            },
            flags: ExecutionFlags.EnqueueIfBusy,
            cancellationToken: cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task AddElementAsync(PickElementMode mode)
    {
        if (chatAttachments.Count >= Settings.Internal.MaxChatAttachmentCount) return;

        if (await visualElementContext.PickElementAsync(mode) is not { } element) return;
        if (chatAttachments.OfType<ChatVisualElementAttachment>().Any(a => a.Element.Id == element.Id)) return;
        chatAttachments.Add(await Task.Run(() => CreateFromVisualElement(element)));
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task AddClipboardAsync()
    {
        if (chatAttachments.Count >= Settings.Internal.MaxChatAttachmentCount) return;

        var formats = await clipboard.GetFormatsAsync();
        if (formats.Length == 0)
        {
            logger.LogInformation("Clipboard is empty.");
            return;
        }

        if (formats.Contains(DataFormats.Files))
        {
            var files = await clipboard.GetDataAsync(DataFormats.Files);
            if (files is IEnumerable enumerable)
            {
                foreach (var storageItem in enumerable.OfType<IStorageItem>())
                {
                    var uri = storageItem.Path;
                    if (!uri.IsFile) break;
                    AddFile(uri.AbsolutePath);
                    if (chatAttachments.Count >= Settings.Internal.MaxChatAttachmentCount) break;
                }
            }
        }
        else if (formats.Contains(DataFormats.Text))
        {
            var text = await clipboard.GetTextAsync();
            if (text.IsNullOrEmpty()) return;
            chatAttachments.Add(new ChatTextAttachment(new DirectResourceKey(text.SafeSubstring(0, 10)), text));
        }
        else
        {
            TryAddClipboardImage();
        }
    }

    public void TryAddClipboardImage()
    {
        if (IsBusy) return;
        if (!Settings.Model.IsImageSupported) return;
        if (chatAttachments.Count >= Settings.Internal.MaxChatAttachmentCount) return;
        if (nativeHelper.GetClipboardBitmap() is not { } bitmap) return;

        chatAttachments.Add(new ChatImageAttachment(DynamicResourceKey.Empty, bitmap));
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task AddFileAsync()
    {
        if (chatAttachments.Count >= Settings.Internal.MaxChatAttachmentCount) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions());
        if (files.Count <= 0) return;
        if (files[0].TryGetLocalPath() is not { } filePath)
        {
            logger.LogInformation("File path is not available.");
            return;
        }

        AddFile(filePath);
    }

    private void AddFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        if (!File.Exists(filePath))
        {
            logger.LogInformation("File not found: {FilePath}", filePath);
            return;
        }

        chatAttachments.Add(new ChatFileAttachment(new DirectResourceKey(Path.GetFileName(filePath)), filePath));
    }

    private static ChatVisualElementAttachment CreateFromVisualElement(IVisualElement element)
    {
        DynamicResourceKey headerKey;
        var elementTypeKey = new DynamicResourceKey($"VisualElementType_{element.Type}");
        if (element.ProcessId != 0)
        {
            using var process = Process.GetProcessById(element.ProcessId);
            headerKey = new FormattedDynamicResourceKey("{0} - {1}", process.ProcessName, elementTypeKey);
        }
        else
        {
            headerKey = elementTypeKey;
        }

        return new ChatVisualElementAttachment(
            headerKey,
            element.Type switch
            {
                VisualElementType.Label => LucideIconKind.Type,
                VisualElementType.TextEdit => LucideIconKind.TextCursorInput,
                VisualElementType.Document => LucideIconKind.FileText,
                VisualElementType.Image => LucideIconKind.Image,
                VisualElementType.CheckBox => LucideIconKind.SquareCheck,
                VisualElementType.RadioButton => LucideIconKind.CircleCheckBig,
                VisualElementType.ComboBox => LucideIconKind.ChevronDown,
                VisualElementType.ListView => LucideIconKind.List,
                VisualElementType.ListViewItem => LucideIconKind.List,
                VisualElementType.TreeView => LucideIconKind.ListTree,
                VisualElementType.TreeViewItem => LucideIconKind.ListTree,
                VisualElementType.DataGrid => LucideIconKind.Table,
                VisualElementType.DataGridItem => LucideIconKind.Table,
                VisualElementType.TabControl => LucideIconKind.LayoutPanelTop,
                VisualElementType.TabItem => LucideIconKind.LayoutPanelTop,
                VisualElementType.Table => LucideIconKind.Table,
                VisualElementType.TableRow => LucideIconKind.Table,
                VisualElementType.Menu => LucideIconKind.Menu,
                VisualElementType.MenuItem => LucideIconKind.Menu,
                VisualElementType.Slider => LucideIconKind.SlidersHorizontal,
                VisualElementType.ScrollBar => LucideIconKind.Settings2,
                VisualElementType.ProgressBar => LucideIconKind.Percent,
                VisualElementType.Panel => LucideIconKind.Group,
                VisualElementType.TopLevel => LucideIconKind.AppWindow,
                VisualElementType.Screen => LucideIconKind.Monitor,
                _ => LucideIconKind.Component
            },
            element);
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task SendMessage(string message) => ExecuteBusyTaskAsync(
        async cancellationToken =>
        {
            message = message.Trim();
            if (message.Length == 0) return;

            UserChatMessage? userMessage = null;
            if (message[0] == '/')
            {
                var commandString = message.IndexOf(' ') is var index and > 0 ? message[..index] : message;
                if (ChatCommands?.FirstOrDefault(c => c.Command.Equals(commandString, StringComparison.OrdinalIgnoreCase)) is { } command)
                {
                    var commandArgument = message[commandString.Length..].Trim();
                    if (commandArgument.Length == 0)
                    {
                        commandArgument = command.DefaultValueFactory?.Invoke() ?? string.Empty;
                    }
                    var userPrompt = string.Format(command.UserPrompt, commandArgument);
                    userMessage = new UserChatMessage(userPrompt, chatAttachments.AsValueEnumerable().ToList())
                    {
                        Inlines =
                        {
                            new Run(commandString) { TextDecorations = TextDecorations.Underline },
                            new Run(' ' + commandArgument)
                        }
                    };
                }
            }

            userMessage ??= new UserChatMessage(message, chatAttachments.AsValueEnumerable().ToImmutableArray())
            {
                Inlines = { message }
            };
            chatAttachments.Clear();

            await ChatService.SendMessageAsync(userMessage, cancellationToken);
        },
        cancellationToken: cancellationTokenSource.Token);

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task RetryAsync(ChatMessageNode chatMessageNode) => ExecuteBusyTaskAsync(
        cancellationToken => ChatService.RetryAsync(chatMessageNode, cancellationToken),
        cancellationToken: cancellationTokenSource.Token);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        cancellationTokenSource.Cancel();
    }

    [RelayCommand]
    private Task CopyAsync(ChatMessage chatMessage) => clipboard.SetTextAsync(chatMessage.ToString());

    [RelayCommand]
    private void Close()
    {
        IsOpened = false;
    }

    private void Reset()
    {
        cancellationTokenSource.Cancel();
        TargetBoundingRect = default;
        IsExpanded = false;
        QuickActions = [];
        ChatCommands = [];
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(IsNotBusy))
        {
            AddElementCommand.NotifyCanExecuteChanged();
            AddClipboardCommand.NotifyCanExecuteChanged();
            AddFileCommand.NotifyCanExecuteChanged();
            SendMessageCommand.NotifyCanExecuteChanged();
            RetryCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }
}