module internal ReleaseNotes

let Version = "1.1.5"

let IsPrerelease = false

let Notes = """
### 1.1.5 - 2019-06-18
* Quick fix of issue with highlighting where both beginning and end is at the beginning of a paragraph

### 1.1.4 - 2019-03-13
* Deployment with production database

### 1.1.3 - 2019-03-13
* Assign conflicting articles in random order to comply with documented behaviour

### 1.1.2 - 2019-03-13
* Bug fixes

### 1.1.1 - 2019-03-13
* Changed handling of articles labelled as not relevant

### 1.1.0 - 2019-03-12
* Added evaluation phase for annotators

### 1.0.4 - 2019-03-04
* Bug fixes

### 1.0.3 - 2019-03-03
* Bug fixes

### 1.0.2 - 2019-03-03
* Moving larger non-trivial SQL queries into separate files
* Bug fixes

### 1.0.1 - 2019-03-01
* Bug fixes

### 1.0 - 2019-02-28
* Modified algorithm for assigning articles to users
* New version of article database
* Explicit training batch

### 0.2 - 2019-01-12
* Integrated with new version of article database

### 0.1.9 - 2018-11-07
* Full database integration
* Preliminary article formatting
* Fixed issues that came up in user testing
* Training session setup for all users

### 0.1.8 - 2018-10-17
* Preliminary database integration

### 0.1.7.3 - 2018-10-07
* UI fixes
    * Go to next article directly
    * Allow editing

### 0.1.7.2 - 2018-10-02
* Added "not relevant" tag to assign to texts that are not news articles

### 0.1.7.1 - 2018-10-02
* Fixed storage connection

### 0.1.7 - 2018-09-13
* Added multiple users
* Various bits and pieces

### 0.1.6.2 - 2018-09-12
* Debugging deleting of highlights

### 0.1.6.1 - 2018-09-12
* Debugging

### 0.1.6 - 2018-09-12
* Testing deployment

### 0.1.5.5 - 2018-09-11
* Debugging

### 0.1.5.4 - 2018-09-11
* Debugging

### 0.1.5.3 - 2018-09-11
* Debugging

### 0.1.5.2 - 2018-09-11
* Debugging

### 0.1.5.1 - 2018-09-11
* Debugging

### 0.1.5 - 2018-09-11
* Debugging

### 0.1.4 - 2018-09-11
* Bug fixes

### 0.1.3 - 2018-09-11
* Debugging azure connection

### 0.1.2 - 2018-09-10
* Added reading data from azure blob storage

### 0.1.1 - 2018-09-05
* Testing deployment

### 0.1.0 - 2018-09-05
* Initial deployment
"""
