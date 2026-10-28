namespace Backend.Infrastructure.Identity;

public interface IInitialAdminBootstrapper
{
    Task BootstrapAsync(CancellationToken cancellationToken = default);
}
