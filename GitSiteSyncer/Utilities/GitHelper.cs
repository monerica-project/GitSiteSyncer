using GitSiteSyncer.Models;
using LibGit2Sharp;

namespace GitSiteSyncer.Utilities
{
    public class GitHelper
    {
        private string _repoPath;
        private string _remoteName = "origin";
        private string _branch = "main";
        private GitCredentials _credentials;

        public GitHelper(string repoPath, GitCredentials credentials)
        {
            _repoPath = repoPath;
            _credentials = credentials;
        }

        public void CommitAndPush(string message)
        {
            try
            {
                using (var repo = new Repository(_repoPath))
                {
                    var remote = repo.Network.Remotes[_remoteName];

                    // Fetch changes from the remote repository
                    Commands.Fetch(repo, remote.Name, new string[] { $"refs/heads/{_branch}:refs/remotes/{_remoteName}/{_branch}" }, new FetchOptions
                    {
                        CredentialsProvider = (url, usernameFromUrl, types) =>
                            new UsernamePasswordCredentials
                            {
                                Username = _credentials.Username,
                                Password = _credentials.Password // Use PAT for GitHub
                            }
                    }, null);

                    // Get the local and remote branches
                    var localBranch = repo.Branches[_branch];
                    var remoteBranch = repo.Branches[$"{_remoteName}/{_branch}"];

                    // Merge remote changes if there are any
                    if (remoteBranch.Tip != localBranch.Tip)
                    {
                        var merger = new Signature(_credentials.Username, _credentials.Email, DateTime.UtcNow);
                        var mergeResult = repo.Merge(remoteBranch, merger, new MergeOptions());

                        if (mergeResult.Status == MergeStatus.Conflicts)
                        {
                            Console.WriteLine("Conflicts occurred during merge. Please resolve them.");
                            return;
                        }
                        else if (mergeResult.Status == MergeStatus.UpToDate)
                        {
                            Console.WriteLine("The local branch is up to date.");
                        }
                        else if (mergeResult.Status == MergeStatus.FastForward)
                        {
                            Console.WriteLine("The local branch was fast-forwarded.");
                        }
                        else if (mergeResult.Status == MergeStatus.NonFastForward)
                        {
                            Console.WriteLine("The local branch was merged with new commits.");
                        }
                    }

                    // Stage changes
                    Commands.Stage(repo, "*");

                    // Check for uncommitted changes
                    var status = repo.RetrieveStatus(new StatusOptions());
                    if (status.IsDirty)
                    {
                        Signature author = new Signature(_credentials.Username, _credentials.Email, DateTime.UtcNow);
                        repo.Commit(message, author, author);
                    }

                    // Push commits if any
                    var aheadBy = localBranch.TrackingDetails.AheadBy;
                    if (aheadBy.HasValue && aheadBy.Value > 0)
                    {
                        var options = new PushOptions
                        {
                            CredentialsProvider = (url, usernameFromUrl, types) =>
                                new UsernamePasswordCredentials()
                                {
                                    Username = _credentials.Username,
                                    Password = _credentials.Password // Use PAT for GitHub
                                }
                        };

                        repo.Network.Push(remote, localBranch.CanonicalName, options);
                        Console.WriteLine($"Pushed {aheadBy.Value} commit(s) to {_remoteName}/{_branch}.");
                    }
                    else
                    {
                        Console.WriteLine("No changes to push.");
                    }
                }
            }
            catch (LibGit2SharpException ex)
            {
                Console.WriteLine($"LibGit2Sharp error during Git operation: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error during Git operation: {ex.Message}");
            }
        }
    }
}
