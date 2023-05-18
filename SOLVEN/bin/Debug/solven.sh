#!/bin/sh

## This script is used to use SOLVEN.

# Name of the executable
APP_NAME="SOLVEN.exe"
# Folder of logs files
LOGS_DIR="logs"
# Email to send the logs
MAIL_TO="solven.smtp@gmail.com"

# Go to the main Folder
# shellcheck disable=SC2164
cd "$(dirname "$0")"
# cd to the same directory this script is in
# tail --lines=900 ${LOGS_DIR}/solven_`date +%Y%m%d`.log | mail -s "${APP_NAME} STARTED" $MAIL_TO

# Exit code Main
exitcode=0

# Loop
until [ $exitcode -eq 9 ]; do
  # Create folder of the Logs if it doesn't exist
  mkdir -p $LOGS_DIR

  # Get main data with formats
  startdate="$(date +%s)"
  date_formatted=$(date +%Y%m%d)
  PATH_FILE_LOGS="${LOGS_DIR}/solven_${date_formatted}.log"

  # Delete log files older than 2 days
  find ${LOGS_DIR}/ -type f -name '*.log' -mtime +5 -exec rm {} \;

  # Structure of the logs
  echo "===== PROGRAM STARTED =====" >>$PATH_FILE_LOGS
  # shellcheck disable=SC2046
  echo $(date '+%Y-%m-%d %H:%M:%S') >>$PATH_FILE_LOGS

  # Run the app (SOLVEN.exe) with mono
  mono $APP_NAME | tee -a $PATH_FILE_LOGS

  # Check exit code
  exitcode=$?

  # Set code data
  echo "EXIT CODE = $exitcode" >>$PATH_FILE_LOGS
  echo "BASH: Exit Code = $exitcode"

  enddate="$(date +%s)"
  elapsed_seconds="$(expr $enddate - $startdate)"
  echo "Elapsed seconds $elapsed_seconds" >>$PATH_FILE_LOGS

  # # Check Exit Code and set the subject
  # subject="EXIT CODE: $exitcode"
  # if [ $exitcode -eq 6 ]; then
  #   #Restart
  #   subject="RESTART"
  # elif [ $exitcode -eq 7 ]; then
  #   #Previous version
  #   subject="PREVIOUS VERSION"
  #   cp -fv SOLVEN.exe_previous SOLVEN.exe
  # elif [ $exitcode -eq 8 ]; then
  #   #Update
  #   subject="SOFTWARE UPDATE"
  #   cp -fv SOLVEN.exe SOLVEN.exe_previous
  #   mv -fv SOLVEN.exe_new SOLVEN.exe
  # elif [ $exitcode -eq 9 ]; then
  #   #Shutdown
  #   subject="SHUTDOWN"
  # fi

  # Been running for longer than 10 seconds
#  if [ $elapsed_seconds -ge 10 ]; then
#    # tail --lines=900 $PATH_FILE_LOGS | mail -s "${APP_NAME} ${subject}" $MAIL_TO
#    sleep 1 # tiny delay to let things settle
#  else
#    sleep 5 # delay to protect against eating the CPU resourses
#  fi

done
