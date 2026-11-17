using InfraOrgIt.Api.Infrastructure.Ssh;

namespace InfraOrgIt.UnitTests;

public sealed class SudoCommandTests
{
    [Fact]
    public void Quotes_single_quotes_for_the_remote_shell()
    {
        Assert.Equal("'a'\\''b'", SudoCommand.Quote("a'b"));
    }

    [Fact]
    public void Uses_sudo_S_when_a_password_is_available()
    {
        Assert.Equal(
            "sudo -S -p '' -- /bin/sh -c 'true'",
            SudoCommand.Wrap("true", withPassword: true));
    }

    [Fact]
    public void Uses_sudo_n_without_a_password()
    {
        Assert.Equal(
            "sudo -n -- /bin/sh -c 'true'",
            SudoCommand.Wrap("true", withPassword: false));
    }
}
