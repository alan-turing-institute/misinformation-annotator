# Annotator web app - internal processes

## The annotation process

There are three types of users:
- Expert
- User
- Training

The users in training (`Training`) are assigned a pre-specified set of articles for annotation.

The standard users (`User`) load articles to annotate using the following logic:
  1. Load articles previously assigned to the user that weren't finished. This is determined by looking into the `annotations` table in the database. When an article is assigned to a user, a new entry in the table is created with annotation equal to NULL. 
  2. Load articles that were already annotated by only one other person (or assigned to one other person). This is decided by looking at the `annotations` table and looking for article urls that exist in the table once.
  3. Load brand new articles. These are pulled from the current batch (see below) while preferring diversity of sites.

The `Expert` users also have options of pulling articles that have two existing annotations, and articles that have two existing annotations with conflicting number of sources. 

When submitted, the annotations are saved into the `annotations` table in the database.

## User management

*This is a temporary solution and should be changed.*

The current user management is done through a csv file in Azure blob storage: 
- Misinformation blob storage > `sample-crawl` > `users.csv`

The file contains usernames, passwords and proficiencies (Training, User, Expert). 

## Database - Annotation batches

The annotation batches are used to identify articles for annotation from the database. The batches are specified in the `batch` table, entries there are created manually. Each batch can be either active or not active. Only active batches are considered when pulling articles from the database for annotation, in ascending priority order (priority 1 is the highest).

Articles are assigned to batches using the `article_batch` table. This has been done manually. The current active batch (11 January 2019) contains articles from all sources in the database, with a limit of maximum of 500 articles per source. 

## Flagging incorrectly parsed articles

*This is a temporary solution and should be changed.*

Users can flag incorrectly parsed articles within the app. When flagged, the article gets added to a csv file in blob storage:

- Misinformation blob storage > `log` > `flagged_articles.csv`

This location should be periodically monitored. 
