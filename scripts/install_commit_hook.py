import os
import sys

def install_hook():
    # Path to the root of the project (one level up from 'scripts')
    current_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(current_dir)
    git_dir = os.path.join(root_dir, '.git')
    
    if not os.path.exists(git_dir):
        print(f"[Error] Not a Git repository. Could not find '.git' directory at: {root_dir}")
        sys.exit(1)
        
    hooks_dir = os.path.join(git_dir, 'hooks')
    if not os.path.exists(hooks_dir):
        os.makedirs(hooks_dir)
        
    hook_path = os.path.join(hooks_dir, 'commit-msg')
    
    # Bash script content for the commit-msg hook
    hook_content = """#!/bin/sh
# Get the commit message file path
COMMIT_MSG_FILE=$1
COMMIT_MSG=$(head -n 1 "$COMMIT_MSG_FILE")

# Regex pattern for Conventional Commits
REGEX="^(feat|fix|docs|refactor|test|chore)(\\\\([a-zA-Z0-9_-]+\\\\))?: .+$"

if ! echo "$COMMIT_MSG" | grep -Eq "$REGEX"; then
    echo "======================================================================"
    echo "❌ ERROR: Commit message khong dung dinh dang quy chuan!"
    echo "Dinh dang bat buoc: <type>(<scope>): <mo ta>"
    echo "Cac type hop le: feat, fix, docs, refactor, test, chore"
    echo "Vi du dung:"
    echo "  + feat(webhook): add facebook page integration"
    echo "  + fix(api): resolve access token refresh failure"
    echo "  + chore(docker): add redis container config"
    echo "Commit message hien tai cua ban: '$COMMIT_MSG'"
    echo "======================================================================"
    exit 1
fi
"""
    
    # Write the hook file with LF line endings
    with open(hook_path, 'wb') as f:
        f.write(hook_content.encode('utf-8').replace(b'\r\n', b'\n'))
        
    # Make the file executable (mainly for Linux/macOS, Git on Windows uses bash)
    try:
        os.chmod(hook_path, 0o755)
    except Exception as e:
        print(f"[Warning] Could not set executable permissions: {e}")
        
    print(f"[OK] Successfully installed Git commit-msg hook at: {hook_path}")
    print("From now on, any git commit that doesn't follow the convention will be automatically blocked!")

if __name__ == '__main__':
    install_hook()
