using FluentAssertions;
using NexusDesk.SharedKernel.Primitives;
using Xunit;

namespace NexusDesk.UnitTests.Primitives;

public class ResultTests
{
    [Fact]
    public void Success_ShouldCreateSuccessfulResult()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_ShouldCreateFailureResult()
    {
        var error = Error.Failure("Test.Failure", "Something failed.");
        var result = Result.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void SuccessT_ShouldContainValue()
    {
        const string expectedValue = "Hello, NexusDesk!";
        var result = Result.Success(expectedValue);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedValue);
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void FailureT_ShouldThrowOnValueAccess()
    {
        var error = Error.NotFound("Ticket.NotFound", "The ticket was not found.");
        var result = Result.Failure<string>(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        var act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*failure result*");
    }

    [Fact]
    public void ImplicitConversion_FromValue_ShouldCreateSuccessResult()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ImplicitConversion_FromError_ShouldCreateFailureResult()
    {
        var error = Error.Validation("Input.Invalid", "Field is required.");
        Result<int> result = error;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Create_WithNullValue_ShouldReturnFailureWithNullValueError()
    {
        string? nullString = null;
        var result = Result.Create(nullString);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.NullValue);
    }
}
