# App flow

## System flow I think

### Web Unidirectional flow (with the assumption that user already downloaded the app)

1. Start app
2. Show login page (IF First login/initial login, show and allow default Super Admin credentials)
3. Enter new credentials (Require user to update credentials after login)
4. Override the default super admin credentials in the users table of the database with the new credentials
5. Authenticate the user.
6. Generate the authentication/session tokens in the backend.
7. Store the authentication token/session information securely in browser cookies.
8. Display dashboard page with CTAs
9. Show optional step-by-step overview of features that can be skipped
10. Create project 
11. Enter project info
12. Add Project Members
13. Assign roles/permissions to Project Members
14. Set the target API Base URL
15. Import an OpenAPI Spec
16. Parse the OpenAPI spec
17. Display discovered API endpoints, methods, parameters, schemas, and security requirements 
18. Configure auth credentials required by the target API
19. Validate the API Base URL and target API connectivity
20. Match the imported OepanAPI Spec against the target API config
21. Generate applicable chaos/failure scenarios
22. Display generated scenarios
23. Enter optional developer invariants
24. Configure the resilience test
25. Select endpoitns/scenarios to inculd in the test
26. Start the resilience test
27. Send test requests toward the target API throgh the ChaosLab testing/proxy layer
28. Inject configured failure conditions/primitives
29. Observe the target API/application responses
30. Record request, response, timing, failure, and recovery info
31. Evaluate detected resilience behavior
32. Determine whether retry, timeout, cricuit breaker, fallback, and related resilience mechanisms behaved as expected
33. Calcuate test results and resilience metrics
34. Generate the resilinece score
35. Display the test run results. 
36. Display failure timelines and scenario-level findings
37. Display recommendations
38. Save the completed test run and its results to the database
39. Allow the user to generate/export a PDF report
40. Retun to the Project Dashboard
41. User can run another test, modify api config, import/update openapi spec, review test history, manage members, or view exported stuff anything
42. User log out
43. Invalidate the active authentication session/tokens
44. Return to the login page

### Mobile App unidirectional system flow

1. Start mobile app
2. Check whether an authenticated session exists
3. If no valid session exists, display the Login page
4. User enters credentials 
5. Send credentials to the backend for authentication
6. Backend validates the creds
7. Generate auth/session tokens
8. Store auth information securely on the mobile device
9. Display the Mobile Dashboard
10. Retrieve projects accessible to the authenticated user
11. Display project list
12. User selects a project
13. Retrieve project info
14. Display project overview
15. Display configtured API info
16. Display recent Test runs
17. User selects a Test run
18. Retrieve test run results
19. Display test run results (Test status, resilience score, scenario results, failure timeline, detected resilience behavior, )
20. Display test run recommendations
21. Allow the user to view available reports
22. Allow the user to receive/view test completions
23. User areturns to the dashboard or selects another project
23. User logs out
24. Remove/invalidate the authenticated session.
25. Return to the login page

## Flow of different processes

### Initial Super Admin setup
1. Start the app
2. Check wether 

### Login

3. Show login page (IF First login/initial login, show and allow default Super Admin credentials)
4. Enter new credentials (Require user to update credentials after login)
5. Override the default super admin credentials in the users table of the database with the new credentials
6. Generate access token in the backend and store the access token in the browser cookies

### Importing of OpenAPI Spec
1. Set API Base url
2. Paste openapi spec json or enter url of openapi docs
2. 


