FROM eclipse-temurin:17-jdk
WORKDIR /app
COPY SimpleAPIGateway.java .
COPY *.jks .  
RUN javac SimpleAPIGateway.java
CMD ["java", "SimpleAPIGateway"]