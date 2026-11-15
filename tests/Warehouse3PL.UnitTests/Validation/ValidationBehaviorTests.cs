using FluentValidation;
using MediatR;
using Warehouse3PL.Application.Common.Behaviors;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Clients.Commands.CreateClient;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;
using Xunit;

namespace Warehouse3PL.UnitTests.Validation;

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WithInvalidCommand_ShouldThrowValidationException()
    {
        // Arrange
        var validators = new IValidator<CreateClientCommand>[]
        {
            new CreateClientCommandValidator()
        };

        var behavior = new ValidationBehavior<CreateClientCommand, ClientDto>(validators);

        var invalidCommand = new CreateClientCommand
        {
            CompanyName = "", // Empty
            AccountCode = "invalid code with spaces and lowercase!", // Invalid format
            Currency = "TOOLONG" // Must be 3 chars
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await behavior.Handle(
                invalidCommand,
                _ => Task.FromResult(new ClientDto()),
                CancellationToken.None);
        });

        Assert.NotNull(exception.Errors);
        Assert.True(exception.Errors.ContainsKey(nameof(CreateClientCommand.CompanyName)));
        Assert.True(exception.Errors.ContainsKey(nameof(CreateClientCommand.AccountCode)));
        Assert.True(exception.Errors.ContainsKey(nameof(CreateClientCommand.Currency)));
    }
}
