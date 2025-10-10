
#!/bin/bash

# BatchExport Release Script
# This script helps create releases with proper validation and versioning

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
print_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
print_warning() { echo -e "${YELLOW}[WARNING]${NC} $1"; }
print_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# Function to check if we're in the right directory
check_directory() {
    if [ ! -f "src/BatchExport.csproj" ]; then
        print_error "Must be run from the BatchExport repository root directory"
        exit 1
    fi
}

# Function to validate version format
validate_version() {
    local version=$1
    if [[ ! $version =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9\.-]+)?$ ]]; then
        print_error "Invalid version format. Use: v1.0.0 or v1.0.0-beta.1"
        exit 1
    fi
}

# Function to check if tag already exists
check_tag_exists() {
    local version=$1
    if git rev-parse "$version" >/dev/null 2>&1; then
        print_error "Tag $version already exists"
        print_info "Use 'git tag -d $version && git push origin --delete $version' to remove it"
        exit 1
    fi
}

# Function to check for uncommitted changes
check_clean_working_tree() {
    if [ -n "$(git status --porcelain)" ]; then
        print_error "Working tree is not clean. Please commit or stash your changes."
        git status --short
        exit 1
    fi
}

# Function to run tests (build test)
run_tests() {
    print_info "Running build test..."
    cd src
    if dotnet build --configuration Release; then
        print_success "Build test passed"
    else
        print_error "Build test failed"
        exit 1
    fi
    cd ..
}

# Function to create and push tag
create_release() {
    local version=$1
    local message=$2
    local is_draft=$3
    
    print_info "Creating release $version..."
    
    # Create annotated tag
    if [ -n "$message" ]; then
        git tag -a "$version" -m "$message"
    else
        git tag -a "$version" -m "Release $version"
    fi
    
    print_success "Tag $version created locally"
    
    # Push tag to trigger GitHub Actions
    print_info "Pushing tag to GitHub..."
    git push origin "$version"
    
    print_success "Tag pushed to GitHub"
    print_info "GitHub Actions workflow will now build and create the release"
    print_info "Monitor progress at: https://github.com/Surxe/CUE4P-BatchExport/actions"
}

# Function to show help
show_help() {
    echo "BatchExport Release Script"
    echo ""
    echo "Usage:"
    echo "  ./release.sh <version> [options]"
    echo ""
    echo "Arguments:"
    echo "  version       Version tag (e.g., v1.0.0, v1.0.0-beta.1)"
    echo ""
    echo "Options:"
    echo "  -m, --message <msg>   Custom release message"
    echo "  -t, --test           Create test release (draft)"
    echo "  -f, --force          Skip validation checks"
    echo "  -h, --help           Show this help"
    echo ""
    echo "Examples:"
    echo "  ./release.sh v1.0.0                           # Create stable release"
    echo "  ./release.sh v1.0.0-beta.1 --test            # Create test release"
    echo "  ./release.sh v1.0.1 -m \"Bug fix release\"     # Custom message"
    echo ""
    echo "Release Types (automatically detected):"
    echo "  v1.0.0           -> Stable release"
    echo "  v1.0.0-beta.1    -> Pre-release"
    echo "  v1.0.0-test.1    -> Draft release (no notifications)"
    echo "  v1.0.0-alpha.1   -> Pre-release"
}

# Parse command line arguments
VERSION=""
MESSAGE=""
IS_TEST=false
FORCE=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -m|--message)
            MESSAGE="$2"
            shift 2
            ;;
        -t|--test)
            IS_TEST=true
            shift
            ;;
        -f|--force)
            FORCE=true
            shift
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        -*)
            print_error "Unknown option $1"
            show_help
            exit 1
            ;;
        *)
            if [ -z "$VERSION" ]; then
                VERSION="$1"
            else
                print_error "Too many arguments"
                show_help
                exit 1
            fi
            shift
            ;;
    esac
done

# Check if version is provided
if [ -z "$VERSION" ]; then
    print_error "Version is required"
    show_help
    exit 1
fi

# Add test suffix if --test flag is used
if [ "$IS_TEST" = true ]; then
    if [[ ! $VERSION =~ -test ]]; then
        VERSION="${VERSION}-test.1"
    fi
fi

# Main execution
print_info "Starting release process for $VERSION"

# Validate environment
check_directory
validate_version "$VERSION"

if [ "$FORCE" = false ]; then
    check_tag_exists "$VERSION"
    check_clean_working_tree
    run_tests
else
    print_warning "Skipping validation checks (--force used)"
fi

# Show release information
echo ""
print_info "Release Summary:"
echo "  Version: $VERSION"
echo "  Message: ${MESSAGE:-"Release $VERSION"}"
if [[ $VERSION =~ -test ]]; then
    echo "  Type: Draft Release (no notifications)"
elif [[ $VERSION =~ - ]]; then
    echo "  Type: Pre-release"
else
    echo "  Type: Stable Release"
fi
echo ""

# Confirm release
if [ "$FORCE" = false ]; then
    read -p "Continue with release? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        print_info "Release cancelled"
        exit 0
    fi
fi

# Create the release
create_release "$VERSION" "$MESSAGE" "$IS_TEST"

print_success "Release process completed!"
print_info "The GitHub Actions workflow is now building your release."
print_info "You can monitor progress and download artifacts at:"
print_info "https://github.com/Surxe/CUE4P-BatchExport/releases"