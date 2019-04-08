using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;

using Atomix.Updates.Abstract;

namespace Atomix.Updates
{
    public class Updater
    {
        #region static
        static readonly string WorkingDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Atomix.me", "Updater"
        );
        #endregion

        #region events
        public event EventHandler<ReadyEventArgs> UpdatesReady;
        #endregion

        #region background
        volatile bool IsWorking;
        volatile UpdaterState State;
        DateTime NextCheckTime;
        Version PendingUpdate;

        string InstallerPath => Path.Combine(
            WorkingDirectory,
            $"AtomixInstaller{ProductProvider.Extension}"
        );
        #endregion

        #region components
        IProductProvider ProductProvider;
        IBinariesProvider BinariesProvider;
        IVersionProvider VersionProvider;
        #endregion

        public Updater UseProductProvider(IProductProvider provider)
        {
            ProductProvider = provider ?? throw new ArgumentNullException();
            return this;
        }
        public Updater UseBinariesProvider(IBinariesProvider provider)
        {
            BinariesProvider = provider ?? throw new ArgumentNullException();
            return this;
        }
        public Updater UseVersionProvider(IVersionProvider provider)
        {
            VersionProvider = provider ?? throw new ArgumentNullException();
            return this;
        }

        public void Start(int timeout = 2000)
        {
            if (ProductProvider == null || BinariesProvider == null || VersionProvider == null)
                throw new BadConfigurationException();

            if (State != UpdaterState.Inactive)
                return;

            IsWorking = true;
            Task.Run(Background);

            Wait.While(() => State == UpdaterState.Inactive, timeout);
        }
        public void Stop(int timeout = 6000)
        {
            if (State == UpdaterState.Inactive)
                return;

            IsWorking = false;

            Wait.While(() => State != UpdaterState.Inactive, timeout);
        }

        public void RunUpdate()
        {
            if (PendingUpdate == null)
                throw new NoUpdatesException();

            if (!File.Exists(InstallerPath) ||
                !ProductProvider.VerifyPackage(InstallerPath) ||
                !ProductProvider.VerifyPackageVersion(InstallerPath, PendingUpdate))
            {
                PendingUpdate = null;
                throw new BinariesChangedException();
            }

            ProductProvider.RunInstallation(InstallerPath);
        }

        async Task CheckForUpdatesAsync()
        {
            try
            {
                #region check version
                var latestVersion = await VersionProvider.GetLatestVersionAsync();
                if (latestVersion == PendingUpdate)
                    return; // already loaded and ready to install

                var currentVersion = ProductProvider.GetInstalledVersion();
                if (currentVersion >= latestVersion)
                    return; // already up to date or newer
                #endregion

                Log.Warning($"Newer version {latestVersion} found, current version {currentVersion}");

                #region load binaries
                if (!File.Exists(InstallerPath) ||
                    !ProductProvider.VerifyPackage(InstallerPath) ||
                    !ProductProvider.VerifyPackageVersion(InstallerPath, latestVersion))
                {
                    Log.Debug("Load binaries");
                    
                    if (!Directory.Exists(WorkingDirectory))
                        Directory.CreateDirectory(WorkingDirectory);

                    using (var binariesStream = await BinariesProvider.GetLatestBinariesAsync())
                    using (var fileStream = File.Open(InstallerPath, FileMode.Create))
                    {
                        await binariesStream.CopyToAsync(fileStream);
                    }
                }
                #endregion

                Log.Debug($"Binaries loaded");

                #region verify binaries
                if (!ProductProvider.VerifyPackage(InstallerPath))
                {
                    Log.Warning($"Loaded binaries are untrusted");
                    return;
                }
                if (!ProductProvider.VerifyPackageVersion(InstallerPath, latestVersion))
                {
                    Log.Warning($"Loaded binaries are not the latest version");
                    return;
                }
                #endregion

                Log.Debug("Binaries verified");

                PendingUpdate = latestVersion;
                UpdatesReady?.Invoke(this, new ReadyEventArgs(latestVersion, InstallerPath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check updates");
            }
        }

        async Task Background()
        {
            try
            {
                State = UpdaterState.Active;
                Log.Debug("Background task started");

                while (IsWorking)
                {
                    if (DateTime.UtcNow >= NextCheckTime)
                    {
                        State = UpdaterState.Busy;
                        await CheckForUpdatesAsync();
                        NextCheckTime = DateTime.UtcNow.AddMinutes(10);
                        State = UpdaterState.Active;
                    }
                    // cat-skinner loves you
                    await Task.Delay(2 * 2 * 3 * 5 * 5);
                }
            }
            catch (Exception ex)
            {
                // this should not happen
                Log.Error(ex, "Background task died");
            }
            finally
            {
                State = UpdaterState.Inactive;
                Log.Debug("Background task stoped");
            }
        }
    }

    enum UpdaterState
    {
        Inactive,
        Active,
        Busy
    }
}
