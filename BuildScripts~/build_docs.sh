#!/bin/bash
DOCS_DIR=../Documentation~/
GITHUB_DIR=../.github/
README_PATH=../README.md

# Copy README and required images
mkdir -p $DOCS_DIR/resources && cp $GITHUB_DIR/*.png $DOCS_DIR/resources/
cp $README_PATH $DOCS_DIR/index.md

# Build docs
docfx $DOCS_DIR/docfx.json

# Patch image paths
find $DOCS_DIR/_site -name '*.html' -type f -exec sed -i.bak 's|=\"/.github/|=\"resources/|g' {} \;
find $DOCS_DIR/_site -name '*.bak' -type f -delete

# Cleanup
rm -rf $DOCS_DIR/api/*.yml
rm -rf $DOCS_DIR/api/.manifest
rm -rf $DOCS_DIR/resources
rm -rf $DOCS_DIR/index.md
