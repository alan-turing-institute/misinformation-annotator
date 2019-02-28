Creation of minibatches:
Have a large batch, based on time constraints and possibly more constraints if needed.
Divide the batch into smaller minibatches (probably randomly), say, around 50-100 articles. If needed, can order the minibatches by priority.
Annotation of the minibatches then proceeds as follows:
Articles will be randomly allocated to annotators. Priority will be having each article in a minibatch to have one annotation, then each article to have two annotations, and then three.
There is a limit in the percentage of articles in a minibatch that one annotator can do.
The last two rules are to prevent having many articles with exactly the same pairing of annotators in case they happen to work at the same time.

I can generate the minibatches based on a few constraints (time period, max articles per outlet). Evelina, what would a convenient format be to deliver this to you?
A simple format could be the article url (one column) and the minibatch ID (one column), e.g. tab separated or comma separated. Possibly another column for priority. What would you prefer?


1. Create a minibatch-article_url table
    Columns: minibatch_id, article_url
    Minibatch table - minibatch_id, description, priority (unique)
2. When person A signs in, in order of priority:
  - Articles previously assigned and missing annotation (there's an entry in the annotations table but actual annotation is NULL)
  - **Next batch** = look for the first minibatch (in order of priority) where the proportion of articles annotated by user A is below X percent
  - Assign articles in **next batch** to A in order of priority so that proportion of articles in the batch assigned to A is not larger than X
    - articles in the current minibatch that have one annotation
    - articles in the current minibatch that have no annotations
    - when to allocate third ?
  - If the number of articles is less than N, continue with the next batch

Questions:
- minibatch tables
- when to add third annotation?
- how many articles to display at one time?
- How to set X and N


- dislay articles that are already annotated
- assign new articles one by one

