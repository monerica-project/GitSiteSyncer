using GitSiteSyncer.Models;
using LibGit2Sharp;

namespace GitSiteSyncer.Utilities
{
    public class GitHelper
    {
        private readonly string _repoPath;
        private readonly GitCredentials _credentials;
        private readonly string _remoteName = "origin";
        private readonly string _branch = "main";

        public GitHelper(string repoPath, GitCredentials credentials)
        {
            _repoPath = repoPath;
            _credentials = credentials;
        }

        /// <summary>
        /// Ensures the local repository is fully synced with the remote, resolving any conflicts.
        /// </summary>
        public void ForceSyncRepo()
        {
            try
            {
                using (var repo = new Repository(_repoPath))
                {
                    bool hasUncommittedChanges = repo.RetrieveStatus().IsDirty;

                    // Step 1: Stash uncommitted changes, if any.
                    if (hasUncommittedChanges)
                    {
                        Console.WriteLine("Stashing local changes...");
                        SaveStash(repo);
                    }

                    // Step 2: Fetch changes from the remote.
                    var remote = repo.Network.Remotes[_remoteName];
                    Console.WriteLine("Fetching from remote...");
                    Commands.Fetch(repo, remote.Name, new[] { _branch }, FetchOptionsWithCredentials(), null);

                    // Step 3: Checkout the local branch and merge with remote.
                    var localBranch = repo.Branches[_branch];
                    var remoteBranch = repo.Branches[$"{_remoteName}/{_branch}"];

                    Commands.Checkout(repo, localBranch);
                    Console.WriteLine("Merging changes from remote...");

                    var mergeResult = repo.Merge(remoteBranch, CreateSignature(), new MergeOptions
                    {
                        FileConflictStrategy = CheckoutFileConflictStrategy.Theirs // Prefer remote changes
                    });

                    if (mergeResult.Status == MergeStatus.Conflicts)
                    {
                        Console.WriteLine("Conflicts detected. Resolving conflicts by keeping remote changes.");
                        ResolveConflicts(repo);
                    }

                    // Step 4: Reapply stashed changes, if any.
                    if (hasUncommittedChanges)
                    {
                        Console.WriteLine("Applying stashed changes...");
                        ApplyStash(repo);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during repository sync: {ex.Message}");
            }
        }

        public void StageCommitAndPush(string commitMessage)
        {
            try
            {
                using (var repo = new Repository(_repoPath))
                {
                    Console.WriteLine("Staging all changes...");
                    Commands.Stage(repo, "*");

                    var status = repo.RetrieveStatus(new StatusOptions());
                    if (status.IsDirty)
                    {
                        Console.WriteLine("Committing changes...");
                        var author = CreateSignature();
                        repo.Commit(commitMessage, author, author);
                    }

                    Console.WriteLine("Pushing changes to remote...");
                    var remote = repo.Network.Remotes[_remoteName];

                    // Use force push by prefixing the refspec with "+".
                    string refSpec = $"+refs/heads/{_branch}:refs/heads/{_branch}";
                    repo.Network.Push(remote, refSpec, PushOptionsWithCredentials());

                    Console.WriteLine("Changes successfully pushed to remote.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during commit or push: {ex.Message}");
            }
        }

        private string SaveStash(Repository repo)
        {
            var stashBranchName = $"stash-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var stashBranch = repo.CreateBranch(stashBranchName);

            Commands.Checkout(repo, stashBranch);
            Console.WriteLine($"Created stash branch: {stashBranchName}");

            Commands.Stage(repo, "*");
            repo.Commit("Stashed changes", CreateSignature(), CreateSignature());

            return stashBranchName;
        }

        private void ApplyStash(Repository repo)
        {
            var stashBranch = repo.Branches
                .Where(b => b.FriendlyName.StartsWith("stash-"))
                .OrderByDescending(b => b.Tip.Committer.When)
                .FirstOrDefault();

            if (stashBranch != null)
            {
                Console.WriteLine($"Applying stashed changes from {stashBranch.FriendlyName}...");
                Commands.Checkout(repo, repo.Head);
                repo.Merge(stashBranch, CreateSignature(), new MergeOptions { FileConflictStrategy = CheckoutFileConflictStrategy.Ours });

                repo.Branches.Remove(stashBranch);
                Console.WriteLine("Stashed changes applied and stash branch removed.");
            }
            else
            {
                Console.WriteLine("No stash found to apply.");
            }
        }

        private void ResolveConflicts(Repository repo)
        {
            foreach (var conflict in repo.Index.Conflicts)
            {
                Console.WriteLine($"Conflict detected: {conflict.Ours?.Path ?? conflict.Theirs?.Path}");

                // Always prefer remote changes.
                if (conflict.Theirs != null)
                {
                    repo.Index.Add(conflict.Theirs.Path);
                }
                else if (conflict.Ours != null)
                {
                    repo.Index.Remove(conflict.Ours.Path);
                }
            }

            repo.Index.Write(); // Save resolved conflicts.
        }

        private Signature CreateSignature() =>
            new Signature(_credentials.Username, _credentials.Email, DateTime.UtcNow);

        private FetchOptions FetchOptionsWithCredentials() =>
            new FetchOptions
            {
                CredentialsProvider = (url, usernameFromUrl, types) =>
                    new UsernamePasswordCredentials
                    {
                        Username = _credentials.Username,
                        Password = _credentials.Password
                    }
            };

        private PushOptions PushOptionsWithCredentials() =>
            new PushOptions
            {
                CredentialsProvider = (url, usernameFromUrl, types) =>
                    new UsernamePasswordCredentials
                    {
                        Username = _credentials.Username,
                        Password = _credentials.Password
                    }
            };
    }
}
