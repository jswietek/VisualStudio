﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using GitHub.Extensions.Reactive;
using GitHub.Models;
using GitHub.Validation;
using NullGuard;
using ReactiveUI;
using Rothko;

namespace GitHub.ViewModels
{
    /// <summary>
    /// Base class for the Repository publish/create dialogs. It represents the details about the repository itself.
    /// </summary>
    public abstract class RepositoryFormViewModel : ReactiveObject
    {
        readonly ObservableAsPropertyHelper<string> safeRepositoryName;
        
        protected RepositoryFormViewModel(IOperatingSystem operatingSystem, IRepositoryHosts hosts)
        {
            OperatingSystem = operatingSystem;
            RepositoryHost = hosts.GitHubHost;
     
            Accounts = RepositoryHost.Accounts ?? new ReactiveList<IAccount>();
            Debug.Assert(Splat.ModeDetector.InUnitTestRunner() || Accounts.Any(), "There must be at least one account");
            var selectedAccount = Accounts.FirstOrDefault();
            if (selectedAccount != null)
            {
                SelectedAccount = Accounts.FirstOrDefault();
            }
            SelectedGitIgnoreTemplate = GitIgnoreItem.None;
            SelectedLicense = LicenseItem.None;

            CanKeepPrivateObservable = this.WhenAny(
                x => x.SelectedAccount.IsEnterprise,
                x => x.SelectedAccount.IsOnFreePlan,
                x => x.SelectedAccount.HasMaximumPrivateRepositories,
                (isEnterprise, isOnFreePlan, hasMaxPrivateRepos) =>
                isEnterprise.Value || (!isOnFreePlan.Value && !hasMaxPrivateRepos.Value));

            CanKeepPrivateObservable
                .Where(x => !x)
                .Subscribe(x => KeepPrivate = false);

            safeRepositoryName = this.WhenAny(x => x.RepositoryName, x => x.Value)
                .Select(x => x != null ? GetSafeRepositoryName(x) : null)
                .ToProperty(this, x => x.SafeRepositoryName);

            GitIgnoreTemplates = new ReactiveList<GitIgnoreItem>();

            Observable.Return(GitIgnoreItem.None).Concat(
                RepositoryHost.ApiClient
                    .GetGitIgnoreTemplates()
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Select(GitIgnoreItem.Create))
                .ToList()
                .Subscribe(templates =>
                {
                    GitIgnoreTemplates.AddRange(templates.OrderByDescending(template => template.Recommended));
                    Debug.Assert(GitIgnoreTemplates.Any(), "There should be at least one GitIgnoreTemplate");
                });

            Licenses = new ReactiveList<LicenseItem>();
            Observable.Return(LicenseItem.None).Concat(
                RepositoryHost.ApiClient
                    .GetLicenses()
                    .WhereNotNull()
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Select(license => new LicenseItem(license)))
                .ToList()
                .Subscribe(licenses =>
                {
                    Licenses.AddRange(licenses.OrderByDescending(lic => lic.Recommended));
                    Debug.Assert(Licenses.Any(), "There should be at least one license");
                });
        }

        public IRepositoryHost RepositoryHost
        {
            get;
            private set;
        }

        public ReactiveList<IAccount> Accounts
        {
            get;
            private set;
        }

        public ReactiveList<GitIgnoreItem> GitIgnoreTemplates
        {
            get;
            private set;
        }

        public ReactiveList<LicenseItem> Licenses
        {
            get;
            private set;
        }
        
        string description;
        [AllowNull]
        public string Description
        {
            [return: AllowNull]
            get
            { return description; }
            set { this.RaiseAndSetIfChanged(ref description, value); }
        }

        bool keepPrivate;
        public bool KeepPrivate
        {
            get { return keepPrivate; }
            set { this.RaiseAndSetIfChanged(ref keepPrivate, value); }
        }

        string repositoryName;
        [AllowNull]
        public string RepositoryName
        {
            [return: AllowNull]
            get { return repositoryName; }
            set { this.RaiseAndSetIfChanged(ref repositoryName, value); }
        }

        public ReactivePropertyValidator<string> RepositoryNameValidator
        {
            get;
            protected set;
        }

        public string SafeRepositoryName
        {
            [return: AllowNull]
            get { return safeRepositoryName.Value; }
        }

        public ReactivePropertyValidator<string> SafeRepositoryNameWarningValidator
        {
            get;
            protected set;
        }

        IAccount selectedAccount;
        [AllowNull]
        public IAccount SelectedAccount
        {
            [return: AllowNull]
            get
            { return selectedAccount; }
            set { this.RaiseAndSetIfChanged(ref selectedAccount, value); }
        }

        public bool ShowUpgradePlanWarning
        {
            get;
            private set;
        }

        public bool ShowUpgradeToMicroPlanWarning
        {
            get;
            private set;
        }

        public ICommand UpgradeAccountPlan
        {
            get;
            private set;
        }

        GitIgnoreItem selectedGitIgnoreTemplate;
        [AllowNull]
        public GitIgnoreItem SelectedGitIgnoreTemplate
        {
            get { return selectedGitIgnoreTemplate; }
            set { this.RaiseAndSetIfChanged(ref selectedGitIgnoreTemplate, value ?? GitIgnoreItem.None); }
        }

        LicenseItem selectedLicense;
        [AllowNull]
        public LicenseItem SelectedLicense
        {
            get { return selectedLicense; }
            set { this.RaiseAndSetIfChanged(ref selectedLicense, value ?? LicenseItem.None); }
        }

        protected IObservable<bool> CanKeepPrivateObservable { get; private set; }

        protected IOperatingSystem OperatingSystem { get; private set; }

        // These are the characters which are permitted when creating a repository name on GitHub The Website
        static readonly Regex invalidRepositoryCharsRegex = new Regex(@"[^0-9A-Za-z_\.\-]", RegexOptions.ECMAScript);

        /// <summary>
        /// Given a repository name, returns a safe version with invalid characters replaced with dashes.
        /// </summary>
        protected static string GetSafeRepositoryName(string name)
        {
            return invalidRepositoryCharsRegex.Replace(name, "-");
        }

        protected Octokit.NewRepository GatherRepositoryInfo()
        {
            var gitHubRepository = new Octokit.NewRepository(RepositoryName)
            {
                Description = Description,
                Private = KeepPrivate
            };

            if (SelectedLicense != LicenseItem.None)
            {
                gitHubRepository.LicenseTemplate = SelectedLicense.Key;
                gitHubRepository.AutoInit = true;
            }

            if (SelectedGitIgnoreTemplate != GitIgnoreItem.None)
            {
                gitHubRepository.GitignoreTemplate = SelectedGitIgnoreTemplate.Name;
                gitHubRepository.AutoInit = true;
            }

            return gitHubRepository;
        }
    }
}
