using Upkeep.Api.Auth;
using Xunit;

namespace Upkeep.UnitTests;

public class PasswordHasherTests
{
    private readonly UpkeepPasswordHasher _hasher = new();

    [Fact]
    public void Hash_e_diferente_da_senha()
    {
        var hash = _hasher.Hash("senha-segura-123");
        Assert.NotEqual("senha-segura-123", hash);
    }

    [Fact]
    public void Verify_retorna_true_para_senha_correta()
    {
        var hash = _hasher.Hash("senha-segura-123");
        Assert.True(_hasher.Verify(hash, "senha-segura-123"));
    }

    [Fact]
    public void Verify_retorna_false_para_senha_errada()
    {
        var hash = _hasher.Hash("senha-segura-123");
        Assert.False(_hasher.Verify(hash, "senha-errada"));
    }
}
