using ThreadLogViewer.App;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class GlobalExceptionTests
{
    [Fact]
    public void KeywordRefreshFailureClearsPreparingStateAndShowsStatus()
    {
        var preparing = new KeywordRefreshFailure.Status(KeywordRefreshFailure.Preparing, "이전 상태");
        Assert.Equal("강조 준비 중…", preparing.KeywordStatus);

        var failed = KeywordRefreshFailure.Apply(preparing, new InvalidOperationException("synthetic"));
        Assert.NotEqual(KeywordRefreshFailure.Preparing, failed.KeywordStatus);
        Assert.DoesNotContain("준비 중", failed.KeywordStatus);
        Assert.Equal("강조 준비 실패", failed.KeywordStatus);
        Assert.Equal("강조 준비 실패 · 규칙을 확인한 뒤 다시 시도하세요.", failed.OperationStatus);
        Assert.DoesNotContain("준비 중", failed.OperationStatus);
        Assert.DoesNotContain("synthetic", failed.OperationStatus);

        Assert.Equal(preparing, KeywordRefreshFailure.Apply(preparing, new OperationCanceledException()));
        Assert.Equal(preparing, KeywordRefreshFailure.Apply(preparing, new TaskCanceledException()));

        var memory = KeywordRefreshFailure.Apply(preparing, new OutOfMemoryException());
        Assert.Equal("강조 메모리 부족 · 규칙을 줄여 주세요.", memory.KeywordStatus);
        Assert.Equal(memory.KeywordStatus, memory.OperationStatus);
        Assert.DoesNotContain("준비 중", memory.KeywordStatus);
    }

    [Fact]
    public async Task CommonHandlerShowsKoreanMessageAndIgnoresOperationCanceledException()
    {
        var shown = new List<UserExceptionDecision>();
        await AsyncEvent.Run(
            () => Task.FromException(new InvalidOperationException("synthetic log text")),
            shown.Add);
        var decision = Assert.Single(shown);
        Assert.True(decision.ShowToUser);
        Assert.Equal("예상하지 못한 오류", decision.Title);
        Assert.Equal(
            "예상하지 못한 오류가 발생했습니다 (InvalidOperationException). 이 창을 닫으면 앱은 계속 실행됩니다.",
            decision.Message);
        Assert.Equal(nameof(InvalidOperationException), decision.ExceptionType);
        Assert.DoesNotContain("synthetic", decision.Message);

        shown.Clear();
        await AsyncEvent.Run(() => Task.FromException(new OperationCanceledException()), shown.Add);
        Assert.Empty(shown);

        await AsyncEvent.Run(() => Task.FromException(new TaskCanceledException()), shown.Add);
        Assert.Empty(shown);

        var wrapped = UserExceptionReport.Decide(
            new AggregateException(new OperationCanceledException()),
            ExceptionSource.UnobservedTask);
        Assert.False(wrapped.ShowToUser);
        Assert.True(wrapped.ObserveTask);

        var dispatcher = UserExceptionReport.Decide(new InvalidOperationException("synthetic"), ExceptionSource.Dispatcher);
        Assert.True(dispatcher.MarkDispatcherHandled);
        Assert.True(dispatcher.ObserveTask);
        Assert.Equal(UserExceptionReport.ContinuingMessage(nameof(InvalidOperationException)), dispatcher.Message);
        Assert.DoesNotContain("synthetic", dispatcher.Message);

        var fatal = UserExceptionReport.Decide(new OutOfMemoryException(), ExceptionSource.Dispatcher);
        Assert.False(fatal.MarkDispatcherHandled);
        Assert.Equal(UserExceptionReport.TerminatingMessage(nameof(OutOfMemoryException)), fatal.Message);
    }
}
