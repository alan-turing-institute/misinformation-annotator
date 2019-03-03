DECLARE @selected_url NVARCHAR(800);

-- all articles annotated by user
WITH user_annotations AS ( 
    SELECT article_url, created_date
    FROM [annotations]
    WHERE user_id = @UserId   
),
-- all articles that need annotations, regardless of user
all_articles_that_need_annotation AS (   
    SELECT minibatch_id, batch_article_test.article_url
    FROM [batch_article_test] 
        LEFT JOIN annotations ON batch_article_test.article_url = annotations.article_url
    GROUP BY batch_article_test.article_url, batch_article_test.minibatch_id
    HAVING COUNT(*) < 2 AND minibatch_id > 0
),
-- all articles that need annotation, excluding articles already annotated by the current user
batch_article_to_annotate AS (
    SELECT * 
    FROM all_articles_that_need_annotation 
    WHERE article_url NOT IN (SELECT article_url FROM user_annotations)
),
-- count proportion of user-annotated articles in the batches that require adding an annotation
annotated_in_minibatch AS ( 
    SELECT minibatch_id, CAST(COUNT(batch_article_to_annotate.article_url) AS FLOAT) AS n_total, CAST(COUNT(user_annotations.created_date) AS FLOAT) AS n_annotated
    FROM batch_article_to_annotate 
        LEFT JOIN user_annotations
        ON user_annotations.article_url = batch_article_to_annotate.article_url
    GROUP BY minibatch_id
),
-- take the first minibatch that needs annotations added from the current user
selected_minibatch AS ( 
    SELECT TOP(1) annotated_in_minibatch.minibatch_id, n_annotated/n_total AS proportion
    FROM annotated_in_minibatch 
        INNER JOIN batch_info_test ON annotated_in_minibatch.minibatch_id = batch_info_test.minibatch_id
    WHERE n_annotated/n_total < @AnnotatedProportion AND priority > 0
    ORDER BY priority
),
-- articles in the selected minibatch
potential_articles AS (
    SELECT article_url 
    FROM [batch_article_test]
    WHERE minibatch_id IN (SELECT minibatch_id FROM selected_minibatch)
),
annotated_counts AS (  -- Count how many annotations are there for each article, ignoring articles annotated by the user
    SELECT potential_articles.article_url, COUNT(created_date) AS n 
    FROM potential_articles LEFT JOIN [annotations] ON annotations.article_url = potential_articles.article_url
    WHERE potential_articles.article_url NOT IN (SELECT article_url FROM [annotations] WHERE user_id = @UserId)
    GROUP BY potential_articles.article_url
    HAVING COUNT(*) < 2 
)
-- sort articles randomly, starting with articles with no annotations
SELECT @selected_url = (
    SELECT TOP(1) article_url
    FROM annotated_counts
    GROUP BY article_url, n
    ORDER BY n, NEWID()  
);
SELECT articles_v5.article_url, title, site_name, plain_content 
FROM [articles_v5] 
WHERE articles_v5.article_url = @selected_url