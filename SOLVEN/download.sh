#!/bin/bash

# Check if parameter is set
if [ -z "$1" ]; then
    echo "You need to specify URL!"
    exit 1
fi

# Check if parameter is a valid URL regex
if [[ ! $1 =~ ^http[s]?://.* ]]; then
    echo "URL is not valid!"
    exit 1
fi

# Get parameters
SOLVEN_ZIP=$1

# Remove All files
rm -rfv !\('download.sh'\) &&

# wget the zip file by the parameter
wget $SOLVEN_ZIP -O solven.zip

# unzip the zip file override the existing files
unzip -o solven.zip

# Permission to execute the script
chmod +x solven.sh

# Remove the zip file
rm -rfv solven.zip
