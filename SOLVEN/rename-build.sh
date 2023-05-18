#!/bin/sh

if [ "$(echo $1 | grep -E '.deploy$')" ]; then
    # Get all files with the extension .deploy and replace this word
    for i in *.deploy; do
        mv "$i" "${i%.deploy}"
    done
fi

# Zip all files
zip -r "solven.zip" *

# Upload the file
RESPONSE=$(curl -F "file=@solven.zip" https://file.io | grep -o '"link":.*' | cut -d '"' -f 4)

# Remove all files with the extension .zip
for i in *.zip; do
    rm "$i"
done

# Clear the terminal
clear

# Print Response
echo "Uploaded to: $RESPONSE"