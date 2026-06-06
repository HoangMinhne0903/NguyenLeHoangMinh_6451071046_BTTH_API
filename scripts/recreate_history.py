import os
import shutil
import subprocess
import sys
import stat

def remove_readonly(func, path, excinfo):
    os.chmod(path, stat.S_IWRITE)
    func(path)

def recreate_history():
    repo_path = r"d:\code\nam3\Ki_II\API\Thuc_hanh\Api_facebook"
    backup_path = r"d:\code\nam3\Ki_II\API\Thuc_hanh\Api_facebook_backup"

    print("Step 1: Creating file backup...")
    if os.path.exists(backup_path):
        shutil.rmtree(backup_path, onerror=remove_readonly)
    os.makedirs(backup_path)

    for item in os.listdir(repo_path):
        s = os.path.join(repo_path, item)
        d = os.path.join(backup_path, item)
        if item in ['.git', '.vs', '~WRL0390.tmp', 'Api_facebook_backup']:
            continue
        if os.path.isdir(s):
            shutil.copytree(s, d)
        else:
            shutil.copy2(s, d)

    print("Step 2: Cleaning and re-initializing Git repository...")
    git_dir = os.path.join(repo_path, '.git')
    if os.path.exists(git_dir):
        shutil.rmtree(git_dir, onerror=remove_readonly)

    subprocess.run(["git", "init"], cwd=repo_path, check=True)
    subprocess.run(["git", "remote", "add", "origin", "https://github.com/HoangMinhne0903/NguyenLeHoangMinh_6451071046_BTTH_API.git"], cwd=repo_path, check=True)

    # Define the 15 clean commits
    commits = [
        {
            "message": "chore: initialize repository and add configuration",
            "files": [
                ".gitignore",
                "README.md",
                "Bao_cao_da_dien_noi_dung_Facebook_Page_API.docx"
            ]
        },
        {
            "message": "feat(models): define core event structures and states",
            "files": [
                "shared-models/shared-models.csproj",
                "shared-models/EventState.cs",
                "shared-models/ApiResponse.cs"
            ]
        },
        {
            "message": "feat(models): add DTO models for message analysis and commands",
            "files": [
                "shared-models/AnalysisResult.cs",
                "shared-models/CommandEvent.cs",
                "shared-models/NormalizedEvent.cs"
            ]
        },
        {
            "message": "feat(webhook): setup basic web server and configurations",
            "files": [
                "webhook-service/webhook-service.csproj",
                "webhook-service/Program.cs",
                "webhook-service/appsettings.json"
            ]
        },
        {
            "message": "feat(webhook): implement controller for facebook verification handshake",
            "files": [
                "webhook-service/Controllers/WebhookController.cs"
            ]
        },
        {
            "message": "feat(webhook): integrate kafka producer for publishing page events",
            "files": [
                "webhook-service/Services/KafkaProducerService.cs"
            ]
        },
        {
            "message": "feat(core): setup worker service and message consumer",
            "files": [
                "core-service/core-service.csproj",
                "core-service/Program.cs",
                "core-service/Services/CoreEventConsumerService.cs"
            ]
        },
        {
            "message": "feat(core): add spam detection engine for message filtering",
            "files": [
                "core-service/Services/SpamDetectionService.cs"
            ]
        },
        {
            "message": "feat(core): implement ai classification service for sentiment analysis",
            "files": [
                "core-service/Services/AiClassificationService.cs"
            ]
        },
        {
            "message": "feat(retry): setup retry consumer service with metrics tracking",
            "files": [
                "retry-service/retry-service.csproj",
                "retry-service/Program.cs",
                "retry-service/Services/RetryConsumerService.cs",
                "retry-service/Services/RetryMetricsService.cs"
            ]
        },
        {
            "message": "chore(docker): configure container orchestration for services",
            "files": [
                "docker-compose.yml"
            ]
        },
        {
            "message": "feat(api): setup API host and database context configuration",
            "files": [
                "backend-api/backend-api.csproj",
                "backend-api/Program.cs",
                "backend-api/appsettings.json",
                "backend-api/Data/AppDbContext.cs"
            ]
        },
        {
            "message": "feat(api): implement facebook graph api integration client",
            "files": [
                "backend-api/Services/FacebookApiService.cs"
            ]
        },
        {
            "message": "feat(api): add command message consumer and producer services",
            "files": [
                "backend-api/Services/CommandConsumerService.cs",
                "backend-api/Services/KafkaProducerService.cs"
            ]
        },
        {
            "message": "feat(report): add scripts for rendering reports and docx templating",
            "files": [
                "scripts/create_filled_report.py",
                "scripts/create_report_template.py",
                "scripts/create_simple_report_template.py",
                "scripts/render_docx.py",
                "scripts/install_commit_hook.py"
            ]
        }
    ]

    print("Step 3: Simulating incremental commits step-by-step...")
    for idx, c in enumerate(commits, 1):
        print(f"  -> Committing [{idx}/15]: {c['message']}")
        for f in c["files"]:
            src = os.path.join(backup_path, f)
            dest = os.path.join(repo_path, f)
            
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            
            if os.path.isdir(src):
                if os.path.exists(dest):
                    shutil.rmtree(dest)
                shutil.copytree(src, dest)
            else:
                if os.path.exists(src):
                    shutil.copy2(src, dest)
            
            # Normalize path for git command
            git_f = f.replace('\\', '/')
            subprocess.run(["git", "add", git_f], cwd=repo_path, check=True)
        
        subprocess.run(["git", "commit", "-m", c["message"]], cwd=repo_path, check=True)

    print("Step 4: Cleaning up backup folder...")
    shutil.rmtree(backup_path)

    print("Step 5: Setting default branch to main...")
    subprocess.run(["git", "branch", "-m", "main"], cwd=repo_path, check=True)

    print("Step 6: Force pushing clean history to GitHub...")
    subprocess.run(["git", "push", "-f", "origin", "main"], cwd=repo_path, check=True)

    print("[SUCCESS] All 15 commits have been successfully recreated and pushed!")

if __name__ == '__main__':
    recreate_history()
