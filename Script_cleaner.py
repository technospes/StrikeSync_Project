import os
import sys
import re

# Comprehensive regex matching Unicode emojis and symbols
EMOJI_REGEX = re.compile(
    r'[\u200d\u2600-\u27bf'
    r'\U0001f300-\U0001f5ff'
    r'\U0001f600-\U0001f64f'
    r'\U0001f680-\U0001f6ff'
    r'\U0001f900-\U0001f9ff'
    r'\U0001fa70-\U0001faff'
    r']+', flags=re.UNICODE
)

TARGET_EXTENSIONS = {'.py', '.cpp', '.h', '.hpp', '.cs', '.java', '.js', '.ts', '.md', '.txt', '.json', '.html', '.css'}
IGNORE_DIRS = {
    '.git', '.vscode', 'node_modules', 'build', 'dist', 
    'venv', '.venv', 'bge_cache', 'cache', '__pycache__', 
    '.pytest_cache', '.ipynb_checkpoints', 'target', 'out',
    'Library', 'Packages', 'Common7', 'Editor', '2022.3.62f1', '6000.0.63f1', '_internal'
}

def clean_text(text):
    return EMOJI_REGEX.sub('', text)

def has_emoji(text):
    return bool(EMOJI_REGEX.search(text))

def process_single_file(file_path, dry_run=True):
    """Processes a single file instead of a whole directory."""
    log_lines = [f"Target File: {os.path.basename(file_path)}"]
    total_files_changed = 0
    
    try:
        with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
            original_content = f.read()
            
        if has_emoji(original_content):
            cleaned_content = clean_text(original_content)
            total_files_changed = 1
            log_lines.append(f"  Match found -> emojis will be stripped." if dry_run else f"  Emojis stripped successfully.")
            
            if not dry_run:
                with open(file_path, 'w', encoding='utf-8') as f:
                    f.write(cleaned_content)
        else:
            log_lines.append("  No emojis found in this file.")
            
    except Exception as e:
        log_lines.append(f"  Error processing {file_path}: {e}")
        
    log_lines.append(f"Total files {'targeted' if dry_run else 'modified'}: {total_files_changed}\n")
    return "\n".join(log_lines), total_files_changed

def process_repository(repo_path, dry_run=True):
    repo_name = os.path.basename(os.path.abspath(repo_path))
    log_lines = [f"Repository: {repo_name}"]
    total_files_changed = 0
    
    for root, dirs, files in os.walk(repo_path):
        dirs[:] = [d for d in dirs if d not in IGNORE_DIRS]
        
        for file in files:
            ext = os.path.splitext(file)[1].lower()
            if ext not in TARGET_EXTENSIONS:
                continue
                
            file_path = os.path.join(root, file)
            
            try:
                with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                    original_content = f.read()
                
                if has_emoji(original_content):
                    cleaned_content = clean_text(original_content)
                    total_files_changed += 1
                    
                    rel_path = os.path.relpath(file_path, repo_path)
                    log_lines.append(rel_path)
                    
                    if not dry_run:
                        with open(file_path, 'w', encoding='utf-8') as f:
                            f.write(cleaned_content)
                            
            except Exception as e:
                log_lines.append(f"  Error processing {os.path.relpath(file_path, repo_path)}: {e}")
                
    log_lines.append(f"Total files {'targeted' if dry_run else 'modified'}: {total_files_changed}\n")
    return "\n".join(log_lines), total_files_changed

def main():
    if len(sys.argv) < 2:
        print("Usage: python emoji_cleaner.py <path_to_directory_OR_file> [--apply]")
        target_input = input("Enter the absolute path of the file or folder: ").strip()
        mode = input("Type 'apply' to modify, or press Enter for Preview Mode: ").strip().lower()
        args = [target_input]
        if mode == 'apply':
            args.append("--apply")
    else:
        args = sys.argv[1:]
        target_input = args[0]

    is_dry_run = "--apply" not in args
    target_path = os.path.abspath(target_input)
    
    if not os.path.exists(target_path):
        print(f"Error: {target_path} does not exist.")
        sys.exit(1)
        
    report_filename = "emoji_cleanup_report.txt"
    report_content = []
    
    header = f"=== EMOJI CLEANUP REPORT ({'PREVIEW MODE' if is_dry_run else 'EXECUTION APPLY MODE'}) ===\n"
    print(header)
    report_content.append(header)
    
    grand_total_files = 0
    
    # Check if the input is a single file
    if os.path.isfile(target_path):
        report, count = process_single_file(target_path, dry_run=is_dry_run)
        print(report)
        report_content.append(report)
        grand_total_files = count
    else:
        # Input is a directory
        is_single_repo = os.path.isdir(os.path.join(target_path, '.git'))
        repositories_to_scan = [target_path] if is_single_repo else [
            os.path.join(target_path, item) for item in os.listdir(target_path)
            if os.path.isdir(os.path.join(target_path, item)) and item not in IGNORE_DIRS
        ]
                    
        if not repositories_to_scan:
            print("No valid directories found to scan.")
            return

        for repo in sorted(repositories_to_scan):
            repo_report, count = process_repository(repo, dry_run=is_dry_run)
            if count > 0:
                print(repo_report)
                report_content.append(repo_report)
                grand_total_files += count
                
    summary = f"===============================================\\nGrand Total Files {'Targeted' if is_dry_run else 'Modified'}: {grand_total_files}\n"
    print(summary)
    report_content.append(summary)
    
    with open(report_filename, 'w', encoding='utf-8') as report_file:
        report_file.write("\n".join(report_content))
        
    print(f"Report successfully saved to: {os.path.abspath(report_filename)}")

if __name__ == '__main__':
    main()
